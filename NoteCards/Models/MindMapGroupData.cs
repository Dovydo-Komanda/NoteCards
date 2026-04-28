namespace NoteCards.Models;

public class MindMapGroupData
{
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
}