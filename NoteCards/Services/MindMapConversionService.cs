using NoteCards.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NoteCards.Services;

public sealed class MindMapConversionService
{
    private const int MaxSourceCharacters = 9000;
    private const int MaxNodeTextLength = 110;

    public async Task<MindMapNode?> ConvertToMindMapAsync(
        string noteTitle,
        string noteText,
        IProgress<BundledModelHostService.FlashcardProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(noteText))
            return null;

        var normalizedTitle = NormalizeNodeText(noteTitle);
        var source = noteText.Trim();
        if (source.Length > MaxSourceCharacters)
            source = source[..MaxSourceCharacters];

        var prompt = BuildPrompt(normalizedTitle, source);
        var output = await BundledModelHostService.Instance.CompleteAsync(
            prompt, nPredict: 1800, temperature: 0.15, progress: progress, cancellationToken);

        var aiMap = ParseMindMap(output, normalizedTitle);
        if (IsUsefulMindMap(aiMap))
            return aiMap;

        var repairPrompt = BuildRepairPrompt(normalizedTitle, source, output);
        var repaired = await BundledModelHostService.Instance.CompleteAsync(
            repairPrompt, nPredict: 1800, temperature: 0.1, progress: progress, cancellationToken);

        var repairedMap = ParseMindMap(repaired, normalizedTitle);
        if (IsUsefulMindMap(repairedMap))
            return repairedMap;

        var fallback = BuildFallbackMindMap(normalizedTitle, noteText);
        if (IsUsefulMindMap(fallback))
            return fallback;

        var diagnosticPath = WriteParseDiagnostic(noteText, output, repaired);
        throw new InvalidOperationException($"Unable to parse AI output into a valid mind map. Diagnostic log: {diagnosticPath}");
    }

    private static string BuildPrompt(string noteTitle, string sourceNote)
    {
        var title = string.IsNullOrWhiteSpace(noteTitle) ? "Main topic" : noteTitle;
        return $"""
You are a strict mind map generator.
Never think out loud. Never explain. Never output reasoning, analysis, <think> tags, markdown fences, or extra text.

Convert SOURCE NOTE into a hierarchical mind map.
The main topic or note title must be the central idea.
Subsections and key points must become child branches and sub-branches.
Use ONLY information from SOURCE NOTE.
If the note is not suitable for a hierarchy, still create the best concise hierarchy from the visible concepts.

Output ONLY in this exact indentation format:

ROOT: {title}
- Main branch
  - Child point
    - Sub point
- Another main branch

Rules:
- Use 3 to 8 main branches when possible.
- Keep node text concise, under 12 words.
- Do not number branches.
- Do not add facts not present in the note.

SOURCE NOTE:
{sourceNote}
""";
    }

    private static string BuildRepairPrompt(string noteTitle, string sourceNote, string previousOutput)
    {
        var title = string.IsNullOrWhiteSpace(noteTitle) ? "Main topic" : noteTitle;
        return $"""
Repair the mind map output.
Ignore any noisy text, reasoning, comments, or markdown fences.
Use ONLY SOURCE NOTE and output ONLY this format:

ROOT: {title}
- Main branch
  - Child point
    - Sub point

SOURCE NOTE:
{sourceNote}

PREVIOUS OUTPUT:
{previousOutput}
""";
    }

    private static MindMapNode? ParseMindMap(string rawOutput, string fallbackTitle)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return null;

        var cleaned = RemoveThinkAndArtifacts(rawOutput);
        var lines = cleaned
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            .ToList();

        if (lines.Count == 0)
            return null;

        var rootText = NormalizeNodeText(fallbackTitle);
        var root = new MindMapNode
        {
            Text = string.IsNullOrWhiteSpace(rootText)
                ? NormalizeNodeText(lines[0])
                : rootText
        };

        var stack = new List<(int Level, MindMapNode Node)> { (-1, root) };
        var parsedNodes = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (TryParseRootLine(line, out var parsedRoot))
            {
                root.Text = parsedRoot;
                continue;
            }

            if (!TryParseTreeLine(line, out var level, out var nodeText))
                continue;

            nodeText = NormalizeNodeText(nodeText);
            if (string.IsNullOrWhiteSpace(nodeText))
                continue;

            while (stack.Count > 0 && stack[^1].Level >= level)
                stack.RemoveAt(stack.Count - 1);

            var parent = stack.Count == 0 ? root : stack[^1].Node;
            var node = new MindMapNode { Text = nodeText };
            parent.Children.Add(node);
            stack.Add((level, node));
            parsedNodes++;
        }

        if (parsedNodes == 0)
            return null;

        PruneEmptyNodes(root);
        return root;
    }

    private static bool TryParseRootLine(string line, out string rootText)
    {
        var match = Regex.Match(line.Trim(), @"^(?:ROOT|CENTRAL|CENTER|TITLE|MAIN TOPIC)\s*[:：-]\s*(.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            rootText = NormalizeNodeText(match.Groups[1].Value);
            return !string.IsNullOrWhiteSpace(rootText);
        }

        rootText = string.Empty;
        return false;
    }

    private static bool TryParseTreeLine(string line, out int level, out string text)
    {
        var heading = Regex.Match(line, @"^(?<marks>#{1,6})\s+(?<text>.+)$");
        if (heading.Success)
        {
            level = Math.Max(0, heading.Groups["marks"].Value.Length - 1);
            text = heading.Groups["text"].Value;
            return true;
        }

        var bullet = Regex.Match(line, @"^(?<indent>\s*)(?:[-*•]\s+|\d+[\.)]\s+)(?<text>.+)$");
        if (!bullet.Success)
        {
            level = 0;
            text = string.Empty;
            return false;
        }

        var indent = bullet.Groups["indent"].Value.Replace("\t", "    ", StringComparison.Ordinal).Length;
        level = Math.Max(0, indent / 2);
        text = bullet.Groups["text"].Value;
        return true;
    }

    private static MindMapNode? BuildFallbackMindMap(string noteTitle, string noteText)
    {
        var lines = noteText
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
            return null;

        var root = new MindMapNode
        {
            Text = string.IsNullOrWhiteSpace(noteTitle)
                ? NormalizeNodeText(lines[0])
                : NormalizeNodeText(noteTitle)
        };

        var stack = new List<(int Level, MindMapNode Node)> { (-1, root) };
        var parsed = 0;

        foreach (var line in lines)
        {
            if (!TryParseTreeLine(line, out var level, out var text))
                continue;

            text = NormalizeNodeText(text);
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, root.Text, StringComparison.OrdinalIgnoreCase))
                continue;

            while (stack.Count > 0 && stack[^1].Level >= level)
                stack.RemoveAt(stack.Count - 1);

            var parent = stack.Count == 0 ? root : stack[^1].Node;
            var node = new MindMapNode { Text = text };
            parent.Children.Add(node);
            stack.Add((level, node));
            parsed++;
        }

        if (parsed > 0)
            return root;

        var sentenceCandidates = Regex.Split(noteText, @"(?<=[.!?])\s+")
            .Select(NormalizeNodeText)
            .Where(text => text.Length >= 12)
            .Take(6)
            .ToList();

        foreach (var sentence in sentenceCandidates)
            root.Children.Add(new MindMapNode { Text = sentence });

        return root;
    }

    private static bool IsUsefulMindMap(MindMapNode? root)
    {
        return root is not null
            && !string.IsNullOrWhiteSpace(root.Text)
            && root.Children.Count > 0
            && CountNodes(root) >= 3;
    }

    private static int CountNodes(MindMapNode node)
    {
        return 1 + node.Children.Sum(CountNodes);
    }

    private static void PruneEmptyNodes(MindMapNode node)
    {
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            PruneEmptyNodes(child);
            if (string.IsNullOrWhiteSpace(child.Text))
                node.Children.RemoveAt(i);
        }
    }

    private static string NormalizeNodeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        normalized = Regex.Replace(normalized, @"^\s*(?:[-*•]|\d+[\.)])\s*", string.Empty);
        normalized = Regex.Replace(normalized, @"^(?:ROOT|CENTRAL|CENTER|TITLE|MAIN TOPIC)\s*[:：-]\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = normalized.Trim(' ', '"', '\'', '`', ':', '-', '•');

        if (normalized.Length > MaxNodeTextLength)
            normalized = normalized[..MaxNodeTextLength].TrimEnd() + "...";

        return normalized;
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
        return withoutThink.Trim();
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
            var path = Path.Combine(diagnosticsDir, $"mindmap-parse-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

            var sb = new StringBuilder();
            sb.AppendLine($"TimestampUtc: {DateTime.UtcNow:O}");
            sb.AppendLine("--- SOURCE NOTE ---");
            sb.AppendLine(sourceNote);
            sb.AppendLine();
            sb.AppendLine("--- FIRST OUTPUT ---");
            sb.AppendLine(firstOutput);
            sb.AppendLine();
            sb.AppendLine("--- REPAIR OUTPUT ---");
            sb.AppendLine(repairedOutput);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
        catch
        {
            return "(failed to write parse diagnostic log)";
        }
    }
}
