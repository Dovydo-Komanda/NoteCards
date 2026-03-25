namespace NoteCards.Models;

public class AppSettings
{
    public string Language { get; set; } = "en";

    public string Theme { get; set; } = "Light";

    public bool EnableScrollbar { get; set; } = true;
    
    public bool EnableAutoSave { get; set; } = true;
    
    public int AutoSaveIntervalSeconds { get; set; } = 30;

    public bool IsRecentSectionExpanded { get; set; } = true;

    public bool IsGroupsSectionExpanded { get; set; } = true;

    public bool IsUngroupedSectionExpanded { get; set; } = true;

    public bool IsGroupsFirst { get; set; } = true;

    public string DefaultSortOrder { get; set; } = "LastModified";

    // Activity Tracking
    public long TotalTimeSpentSeconds { get; set; } = 0;
    public long TotalWordsTyped { get; set; } = 0;
    public long TotalCharactersTyped { get; set; } = 0;
    public DateTime? LastActiveDate { get; set; }
}
