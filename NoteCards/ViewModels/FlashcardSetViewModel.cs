using NoteCards.Localization;
using NoteCards.Models;

namespace NoteCards.ViewModels;

public sealed class FlashcardSetViewModel : ViewModelBase
{
    public FlashcardSetViewModel(FlashcardSetDocument document)
    {
        Document = document;
    }

    public FlashcardSetDocument Document { get; }

    public string Title => string.IsNullOrWhiteSpace(Document.Title)
        ? LocalizationService.GetString("FlashcardSetUntitled")
        : Document.Title.Trim();

    public int CardCount => Document.Cards?.Count ?? 0;

    public string CardCountText => string.Format(LocalizationService.GetString("FlashcardCardsCountFormat"), CardCount);

    public int SetCount => Document.Cards?
        .Select(card => Math.Max(1, card.SetIndex))
        .Distinct()
        .Count() ?? 0;

    public string SetCountText => string.Format(LocalizationService.GetString("FlashcardSetCountFormat"), SetCount);

    public bool HasTags => Document.Tags?.Any(tag => !string.IsNullOrWhiteSpace(tag)) == true;

    public string TagsDisplay => HasTags
        ? string.Join("   ", Document.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => $"#{tag.Trim()}"))
        : string.Empty;

    public bool IsAiGenerated => !string.IsNullOrWhiteSpace(Document.AiModelDisplayName);

    public string AiGeneratedTooltip => IsAiGenerated
        ? string.Format(LocalizationService.GetString("FlashcardsGeneratedWithModel"), Document.AiModelDisplayName.Trim())
        : string.Empty;

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CardCount));
        OnPropertyChanged(nameof(CardCountText));
        OnPropertyChanged(nameof(SetCount));
        OnPropertyChanged(nameof(SetCountText));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsDisplay));
        OnPropertyChanged(nameof(IsAiGenerated));
        OnPropertyChanged(nameof(AiGeneratedTooltip));
    }
}
