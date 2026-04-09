using NoteCards.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NoteCards.Services;

public sealed class FlashcardConversionService
{
    private const int MaxChunkCharacters = 700;

    public async Task<IReadOnlyList<FlashcardItem>> ConvertToFlashcardsAsync(
        string noteText,
        IProgress<BundledModelHostService.FlashcardProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(noteText))
            return Array.Empty<FlashcardItem>();

        var chunks = SplitIntoChunks(noteText);
        var cards = new List<FlashcardItem>();
        var outputs = new List<string>();

        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkPrompt = BuildPrompt(chunks[i], i + 1, chunks.Count);
            var result = await BundledModelHostService.Instance.CompleteAsync(
                chunkPrompt, nPredict: 1200, temperature: 0.25, progress: progress, cancellationToken);

            outputs.Add(result);
            AddParsedFlashcards(cards, result);
        }

        if (cards.Count > 0)
            return DeduplicateFlashcards(cards);

        // Repair pass over the full note, but still ask for a bounded set of cards.
        var repairPrompt = BuildRepairPrompt(noteText);
        var repaired = await BundledModelHostService.Instance.CompleteAsync(
            repairPrompt, nPredict: 1500, temperature: 0.1, progress: progress, cancellationToken);

        var repairedParsed = ParseFlashcards(repaired);
        if (repairedParsed.Count > 0)
            return repairedParsed;

        var diagnosticPath = WriteParseDiagnostic(noteText, string.Join("\n\n--- CHUNK OUTPUT ---\n\n", outputs), repaired);
        throw new InvalidOperationException($"Unable to parse AI output into valid flashcards. Diagnostic log: {diagnosticPath}");
    }

    private static string BuildPrompt(string noteText, int chunkIndex = 1, int chunkCount = 1)
    {
        return $"""
You are a strict flashcard generator. 
Never think out loud. Never explain. Never output reasoning, analysis, <think> tags or any extra text.

You are processing section {chunkIndex} of {chunkCount} from a longer note.
Cover every important fact in this section from beginning to end.
Do not focus only on the opening lines.
Create as many high-quality flashcards as needed from this section.
Use ONLY facts from the note. Make every card atomic and useful for spaced repetition.

Output **ONLY** in this exact format, nothing else:

q: [clear concise question]
a: [short accurate answer]

SOURCE NOTE:
{noteText}
""";
    }

    private static string BuildRepairPrompt(string sourceNote)
    {
        return $"""
You repair flashcard output. 
Ignore any previous noisy text or thinking.
Never think out loud. Never explain. Never output reasoning or extra text.

Use ONLY information from SOURCE NOTE.
Create **exactly 3 to 8** flashcards in the exact format:

q: [question]
a: [answer]

SOURCE NOTE:
{sourceNote}
""";
    }

    private static IReadOnlyList<FlashcardItem> ParseFlashcards(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return Array.Empty<FlashcardItem>();

        var cleaned = RemoveThinkAndArtifacts(rawOutput);

        var pattern = new Regex(
            @"(?im)^\s*(?:q|question|klausimas)\s*[:：-]\s*(?<q>.+?)\s*(?:\r?\n)+\s*(?:a|answer|atsakymas)\s*[:：-]\s*(?<a>.+?)(?=\r?\n\s*(?:q|question|klausimas|a|answer|atsakymas)[:：-]|\z)",
            RegexOptions.Multiline);

        var matches = pattern.Matches(cleaned);
        var items = new List<FlashcardItem>();

        foreach (Match match in matches)
        {
            var question = NormalizeCardText(match.Groups["q"].Value);
            var answer = NormalizeCardText(match.Groups["a"].Value);

            if (!IsValidFlashcard(question, answer))
                continue;

            items.Add(new FlashcardItem { Question = question, Answer = answer });
        }

        if (items.Count == 0)
            items.AddRange(ParseLinePairs(cleaned));

        if (items.Count == 0)
            items.AddRange(ParseInlinePairs(cleaned));

        return items;
    }

    private static void AddParsedFlashcards(ICollection<FlashcardItem> cards, string rawOutput)
    {
        foreach (var card in ParseFlashcards(rawOutput))
            cards.Add(card);
    }

    private static IReadOnlyList<FlashcardItem> DeduplicateFlashcards(IEnumerable<FlashcardItem> cards)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<FlashcardItem>();

        foreach (var card in cards)
        {
            var key = $"{NormalizeCardKey(card.Question)}\u001F{NormalizeCardKey(card.Answer)}";
            if (!seen.Add(key))
                continue;

            unique.Add(card);
        }

        return unique;
    }

    private static IReadOnlyList<string> SplitIntoChunks(string text)
    {
        var chunks = new List<string>();
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        if (normalized.Length <= MaxChunkCharacters)
        {
            chunks.Add(normalized.Trim());
            return chunks;
        }

        var paragraphs = normalized.Split('\n', StringSplitOptions.None);
        var builder = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                AppendLine(builder, string.Empty);
                continue;
            }

            if (paragraph.Length > MaxChunkCharacters)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString().Trim());
                    builder.Clear();
                }

                chunks.AddRange(SplitLongParagraph(paragraph));

                continue;
            }

            if (builder.Length > 0 && builder.Length + paragraph.Length + 1 > MaxChunkCharacters)
            {
                chunks.Add(builder.ToString().Trim());
                builder.Clear();
            }

            AppendLine(builder, paragraph);
        }

        if (builder.Length > 0)
            chunks.Add(builder.ToString().Trim());

        return chunks;
    }

    private static List<string> SplitLongParagraph(string paragraph)
    {
        var chunks = new List<string>();
        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();

        foreach (var word in words)
        {
            if (builder.Length > 0 && builder.Length + word.Length + 1 > MaxChunkCharacters)
            {
                chunks.Add(builder.ToString());
                builder.Clear();
            }

            if (word.Length > MaxChunkCharacters)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString());
                    builder.Clear();
                }

                for (var i = 0; i < word.Length; i += MaxChunkCharacters)
                    chunks.Add(word.Substring(i, Math.Min(MaxChunkCharacters, word.Length - i)));
                continue;
            }

            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(word);
        }

        if (builder.Length > 0)
            chunks.Add(builder.ToString());

        return chunks;
    }

    private static void AppendLine(StringBuilder builder, string line)
    {
        if (builder.Length > 0)
            builder.AppendLine();
        builder.Append(line);
    }

    private static string NormalizeCardKey(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalized.ToLowerInvariant();
    }

    private static IEnumerable<FlashcardItem> ParseInlinePairs(string text)
    {
        var inlinePattern = new Regex(
            @"(?ims)(?:Q(?:uestion)?|Klausimas)\s*[:：\-]\s*(?<q>.+?)\s+(?:A(?:nswer)?|Atsakymas)\s*[:：\-]\s*(?<a>.+?)(?=(?:\s+(?:Card\s*\d+|Q(?:uestion)?|Klausimas)\s*[:：\-])|\z)",
            RegexOptions.Multiline);

        foreach (Match match in inlinePattern.Matches(text))
        {
            var question = NormalizeCardText(match.Groups["q"].Value);
            var answer = NormalizeCardText(match.Groups["a"].Value);

            if (!IsValidFlashcard(question, answer))
                continue;

            yield return new FlashcardItem
            {
                Question = question,
                Answer = answer
            };
        }
    }

    private static IEnumerable<FlashcardItem> ParseLinePairs(string text)
    {
        var lines = text
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? currentQuestion = null;
        foreach (var line in lines)
        {
            if (IsQuestionLine(line, out var q))
            {
                currentQuestion = NormalizeCardText(q);
                continue;
            }

            if (currentQuestion is not null && IsAnswerLine(line, out var a))
            {
                var question = NormalizeCardText(currentQuestion);
                var answer = NormalizeCardText(a);

                currentQuestion = null;

                if (!IsValidFlashcard(question, answer))
                    continue;

                yield return new FlashcardItem
                {
                    Question = question,
                    Answer = answer
                };
            }
        }
    }

    private static bool IsQuestionLine(string line, out string content)
    {
        return TryStripPrefix(line, ["q", "question", "klausimas", "k"], out content);
    }

    private static bool IsAnswerLine(string line, out string content)
    {
        return TryStripPrefix(line, ["a", "answer", "atsakymas", "ats"], out content);
    }

    private static bool TryStripPrefix(string line, string[] keywords, out string content)
    {
        var trimmed = line.Trim();

        foreach (var keyword in keywords)
        {
            var match = Regex.Match(trimmed, $@"^(?:[-*]\s*)?(?:{Regex.Escape(keyword)})\s*[:：\-]\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                content = match.Groups[1].Value.Trim();
                return true;
            }
        }

        content = string.Empty;
        return false;
    }

    private static string RemoveThinkAndArtifacts(string text)
    {
        var withoutAnsi = Regex.Replace(text, @"\x1B\[[0-9;]*[A-Za-z]", string.Empty);

        var withoutThink = Regex.Replace(withoutAnsi, @"<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        withoutThink = withoutThink.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase);
        withoutThink = withoutThink.Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase);
        withoutThink = Regex.Replace(withoutThink, @"\[Start thinking\].*$", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        withoutThink = Regex.Replace(withoutThink, @"^\s*>.*$", string.Empty, RegexOptions.Multiline);
        withoutThink = Regex.Replace(withoutThink, @"^\s*Thinking Process:.*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        withoutThink = Regex.Replace(withoutThink, @"^\s*\[?end of text\]?\s*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        withoutThink = Regex.Replace(withoutThink, @"^\s*<\|end\|>\s*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        withoutThink = Regex.Replace(withoutThink, @"^\s*EOF\s*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        withoutThink = withoutThink.Replace("EOF by user", string.Empty, StringComparison.OrdinalIgnoreCase);
        withoutThink = withoutThink.Replace("Interrupted by user", string.Empty, StringComparison.OrdinalIgnoreCase);
        return RemoveTrailingArtifacts(withoutThink.Trim());
    }

    private static string NormalizeCardText(string value)
    {
        var normalized = value.Replace("\r", "", StringComparison.Ordinal).Trim();
        normalized = Regex.Replace(normalized, @"^\s*(?:Q(?:uestion)?|Klausimas|A(?:nswer)?|Atsakymas)\s*[:：\-]\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*\[?end of text\]?\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*<\|end\|>\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*EOF\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return RemoveTrailingArtifacts(normalized);
    }

    private static string RemoveTrailingArtifacts(string text)
    {
        var result = text.Trim();

        string[] trailingArtifacts =
        [
            "[end of text]",
            "end of text",
            "<|end|>",
            "EOF",
            "EOF by user",
            "Interrupted by user"
        ];

        foreach (var artifact in trailingArtifacts)
        {
            while (result.EndsWith(artifact, StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^artifact.Length].TrimEnd();
            }
        }

        return result;
    }

    private static bool IsValidFlashcard(string question, string answer)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
            return false;

        if (question.Length < 6 || answer.Length < 3)
            return false;

        if (question.Equals("...", StringComparison.Ordinal) || answer.Equals("...", StringComparison.Ordinal))
            return false;

        if (Regex.IsMatch(question, @"^\s*(?:A(?:nswer)?|Atsakymas)\s*[:：\-]", RegexOptions.IgnoreCase))
            return false;

        if (Regex.IsMatch(answer, @"^\s*(?:Q(?:uestion)?|Klausimas)\s*[:：\-]", RegexOptions.IgnoreCase))
            return false;

        if (LooksLikePromptLeak(question, answer))
            return false;

        return true;
    }

    private static bool LooksLikePromptLeak(string question, string answer)
    {
        var q = question.ToLowerInvariant();
        var a = answer.ToLowerInvariant();

        if (q.Contains("<question>") || a.Contains("<answer>") || q.Contains("q: question") || a.Contains("a: answer"))
            return true;

        string[] leakMarkers =
        [
            "focus on definitions",
            "avoid markdown",
            "no markdown",
            "no bullet lists",
            "no numbering",
            "no reasoning",
            "no analysis",
            "source note",
            "use only information",
            "do not use outside",
            "output format",
            "formatting rules",
            "every card must be exactly 2 lines",
            "do not output reasoning",
            "output contract"
        ];

        if (q.Contains("begin_flashcards") || a.Contains("begin_flashcards") || q.Contains("end_flashcards") || a.Contains("end_flashcards") || q.Contains("<<flashcards>>") || a.Contains("<<flashcards>>") || q.Contains("<<end_flashcards>>") || a.Contains("<<end_flashcards>>"))
            return true;

        return leakMarkers.Any(m => q.Contains(m) || a.Contains(m));
    }

    private static string WriteParseDiagnostic(string sourceNote, string firstOutput, string repairedOutput)
    {
        try
        {
            var diagnosticsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NoteCards",
                "AiCache",
                "diagnostics");

            Directory.CreateDirectory(diagnosticsDir);
            var path = Path.Combine(diagnosticsDir, $"parse-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

            var firstCleaned = RemoveThinkAndArtifacts(firstOutput);
            var secondCleaned = RemoveThinkAndArtifacts(repairedOutput);

            var sb = new StringBuilder();
            sb.AppendLine($"TimestampUtc: {DateTime.UtcNow:O}");
            sb.AppendLine("--- SOURCE NOTE ---");
            sb.AppendLine(sourceNote);
            sb.AppendLine();
            sb.AppendLine("--- FIRST RAW OUTPUT ---");
            sb.AppendLine(firstOutput);
            sb.AppendLine();
            sb.AppendLine("--- FIRST CLEANED/EXTRACTED ---");
            sb.AppendLine(firstCleaned);
            sb.AppendLine();
            sb.AppendLine("--- REPAIR RAW OUTPUT ---");
            sb.AppendLine(repairedOutput);
            sb.AppendLine();
            sb.AppendLine("--- REPAIR CLEANED/EXTRACTED ---");
            sb.AppendLine(secondCleaned);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
        catch
        {
            return "(failed to write parse diagnostic log)";
        }
    }

}
