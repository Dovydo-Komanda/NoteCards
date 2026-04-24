using NoteCards.Localization;
using NoteCards.Models;

namespace NoteCards.ViewModels;

public sealed class MindMapViewModel : ViewModelBase
{
    public MindMapViewModel(MindMapDocument document)
    {
        Document = document;
    }

    public MindMapDocument Document { get; }

    public string Title => string.IsNullOrWhiteSpace(Document.Title)
        ? LocalizationService.GetString("MindMapUntitled")
        : Document.Title.Trim();

    public int NodeCount => CountNodes(Document.Root);

    public string NodeCountText => string.Format(LocalizationService.GetString("MindMapNodeCountFormat"), NodeCount);

    public int BranchCount => Document.Root?.Children?.Count ?? 0;

    public string BranchCountText => string.Format(LocalizationService.GetString("MindMapBranchCountFormat"), BranchCount);

    public bool HasTags => Document.Tags?.Any(tag => !string.IsNullOrWhiteSpace(tag)) == true;

    public string TagsDisplay => HasTags
        ? string.Join("   ", Document.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => $"#{tag.Trim()}"))
        : string.Empty;

    public bool IsAiGenerated => !string.IsNullOrWhiteSpace(Document.AiModelDisplayName);

    public string AiGeneratedTooltip => IsAiGenerated
        ? string.Format(LocalizationService.GetString("MindMapGeneratedWithModel"), Document.AiModelDisplayName.Trim())
        : string.Empty;

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodeCountText));
        OnPropertyChanged(nameof(BranchCount));
        OnPropertyChanged(nameof(BranchCountText));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsDisplay));
        OnPropertyChanged(nameof(IsAiGenerated));
        OnPropertyChanged(nameof(AiGeneratedTooltip));
    }

    private static int CountNodes(MindMapNode? node)
    {
        if (node is null)
            return 0;

        return 1 + node.Children.Sum(CountNodes);
    }
}
