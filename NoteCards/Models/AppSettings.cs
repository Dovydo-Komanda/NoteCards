namespace NoteCards.Models;

public class AppSettings
{
    public string Language { get; set; } = "en";

    public string Theme { get; set; } = "Light";

    public string NoteSortOptionKey { get; set; } = "last-modified-desc";

    public bool EnableScrollbar { get; set; } = true;
    
    public bool EnableAutoSave { get; set; } = true;
    
    public int AutoSaveIntervalSeconds { get; set; } = 30;

    public bool IsRecentSectionExpanded { get; set; } = true;

    public bool IsGroupsSectionExpanded { get; set; } = true;

    public bool IsUngroupedSectionExpanded { get; set; } = true;

    public bool IsGroupsFirst { get; set; } = true;
}
