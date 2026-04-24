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

    public bool IsRecentSectionVisible { get; set; } = true;

    public bool IsGroupsSectionVisible { get; set; } = true;

    public bool IsUngroupedSectionVisible { get; set; } = true;

    public bool IsCalendarSectionExpanded { get; set; } = true;

    public bool IsCalendarSectionVisible { get; set; } = true;

    public bool IsMindMapGroupsSectionExpanded { get; set; } = true;

    public bool IsMindMapGroupsSectionVisible { get; set; } = true;

    public bool IsCalendarFirst { get; set; } = true;

    public string MindMapSortOptionKey { get; set; } = "last-modified-desc";

    public string DefaultViewMode { get; set; } = "Grid";

    public bool IsGroupsFirst { get; set; } = true;

    public string PreferredFontFamily { get; set; } = "Segoe UI";

    public double PreferredFontSize { get; set; } = 14;

    public string FlashcardModelKey { get; set; } = "AutoSelect";
    public List<AiToolSettingsItem> AiTools { get; set; } = new();
    public string? LastView { get; set; }

    public int FlashcardFlipDelayMilliseconds { get; set; } = 300;

    // Activity Tracking
    public long TotalTimeSpentSeconds { get; set; } = 0;
    public long TotalWordsTyped { get; set; } = 0;
    public long TotalCharactersTyped { get; set; } = 0;
    public DateTime? LastActiveDate { get; set; }
}

public class AiToolSettingsItem
{
    public string Key { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public bool IsRemoved { get; set; }
}
