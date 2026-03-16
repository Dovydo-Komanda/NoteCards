namespace NoteCards.Models;

public class AppSettings
{
    public string Language { get; set; } = "en";

    public string Theme { get; set; } = "Light";

    public bool EnableScrollbar { get; set; } = true;

    public bool IsRecentSectionExpanded { get; set; } = true;

    public bool IsGroupsSectionExpanded { get; set; } = true;

    public bool IsUngroupedSectionExpanded { get; set; } = true;

    public bool IsGroupsFirst { get; set; } = true;
}
