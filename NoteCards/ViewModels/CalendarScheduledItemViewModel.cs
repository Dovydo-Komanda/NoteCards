namespace NoteCards.ViewModels;

public sealed class CalendarScheduledItemViewModel
{
    public required NoteCardViewModel Note { get; init; }
    public required DateTime ScheduledAt { get; init; }
    public string ScheduleNote { get; init; } = string.Empty;

    public string Title => Note.Title;
    public string TimeText => ScheduledAt.ToString("HH:mm");
}
