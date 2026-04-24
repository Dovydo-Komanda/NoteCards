namespace NoteCards.Models;

public sealed class MindMapNode
{
    public string Text { get; set; } = string.Empty;
    public List<MindMapNode> Children { get; set; } = new();
    public bool IsExpanded { get; set; } = true;

    public bool HasChildren => Children.Count > 0;
}
