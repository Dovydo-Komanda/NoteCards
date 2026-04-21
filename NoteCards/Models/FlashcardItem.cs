namespace NoteCards.Models;

public sealed class FlashcardItem
{
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int SetIndex { get; init; } = 1;
}
