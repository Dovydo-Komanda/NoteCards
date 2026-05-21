namespace NoteCards.Models;

public class AppSettings
{
    public string Language { get; set; } = "en";

    public string Theme { get; set; } = "Light";

    public string NoteSortOptionKey { get; set; } = "last-modified-desc";

    public bool EnableScrollbar { get; set; } = true;

    public bool EnableVerticalScrollbar { get; set; } = true;

    public bool EnableHorizontalScrollbar { get; set; } = true;
    
    public bool EnableAutoSave { get; set; } = true;
    
    public int AutoSaveIntervalSeconds { get; set; } = 30;

    public bool IsRecentSectionExpanded { get; set; } = true;

    public bool IsGroupsSectionExpanded { get; set; } = true;

    public bool IsUngroupedSectionExpanded { get; set; } = true;

    public bool IsNotesDashboardSectionVisible { get; set; } = true;

    public bool IsNotesDashboardSectionExpanded { get; set; } = true;

    public bool IsRecentSectionVisible { get; set; } = true;

    public bool IsGroupsSectionVisible { get; set; } = true;

    public bool IsUngroupedSectionVisible { get; set; } = true;

    public bool IsCalendarSectionExpanded { get; set; } = true;

    public bool IsCalendarSectionVisible { get; set; } = true;

    public bool IsFlashcardGroupsSectionExpanded { get; set; } = true;

    public bool IsFlashcardGroupsSectionVisible { get; set; } = true;

    public bool IsFlashcardDashboardGroupsSectionVisible { get; set; } = true;

    public bool IsFlashcardDashboardUngroupedSectionVisible { get; set; } = true;

    public bool IsFlashcardDashboardGroupsSectionExpanded { get; set; } = true;

    public bool IsFlashcardDashboardUngroupedSectionExpanded { get; set; } = true;

    public bool IsFlashcardDashboardGroupsFirst { get; set; } = true;

    public bool IsMindMapGroupsSectionExpanded { get; set; } = true;

    public bool IsMindMapDashboardGroupsSectionVisible { get; set; } = true;

    public bool IsMindMapDashboardUngroupedSectionVisible { get; set; } = true;

    public bool IsMindMapDashboardGroupsSectionExpanded { get; set; } = true;

    public bool IsMindMapDashboardUngroupedSectionExpanded { get; set; } = true;

    public bool IsMindMapDashboardGroupsFirst { get; set; } = true;

    public bool IsQuizGroupsSectionVisible { get; set; } = true;

    public bool IsQuizGroupsSectionExpanded { get; set; } = true;

    public bool IsQuizDashboardGroupsSectionVisible { get; set; } = true;

    public bool IsQuizDashboardUngroupedSectionVisible { get; set; } = true;

    public bool IsQuizDashboardGroupsSectionExpanded { get; set; } = true;

    public bool IsQuizDashboardUngroupedSectionExpanded { get; set; } = true;

    public bool IsQuizDashboardGroupsFirst { get; set; } = true;

    public bool IsMindMapGroupsSectionVisible { get; set; } = true;

    public bool IsCalendarFirst { get; set; } = true;

    public string MindMapSortOptionKey { get; set; } = "last-modified-desc";

    public string FlashcardSortOptionKey { get; set; } = "last-modified-desc";

    public string QuizSortOptionKey { get; set; } = "last-modified-desc";

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
