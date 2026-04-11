namespace NoteCards.Models;

public class NoteDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 14;
    public DateTime LastModified { get; set; } = DateTime.Now;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ScheduledAt { get; set; }
    public string ScheduleNote { get; set; } = string.Empty;
    public List<NoteScheduleEntry> Schedules { get; set; } = new();
    public bool IsPinned { get; set; } = false;
    public List<NoteEditHistoryEntry> EditHistory { get; set; } = new();
}

public class NoteScheduleEntry
{
    public DateTime ScheduledAt { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class NoteEditHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Content { get; set; } = string.Empty;
}