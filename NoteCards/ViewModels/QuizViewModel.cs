using NoteCards.Localization;
using NoteCards.Models;

namespace NoteCards.ViewModels;

public sealed class QuizViewModel : ViewModelBase
{
    public QuizViewModel(QuizDocument document)
    {
        Document = document;
    }

    public QuizDocument Document { get; }

    public string Title => string.IsNullOrWhiteSpace(Document.Title)
        ? LocalizationService.GetString("QuizUntitled")
        : Document.Title.Trim();

    public int QuestionCount => Document.Questions?.Count ?? 0;

    public string QuestionCountText => string.Format(LocalizationService.GetString("QuizQuestionCountFormat"), QuestionCount);

    public bool HasTags => Document.Tags?.Any(tag => !string.IsNullOrWhiteSpace(tag)) == true;

    public string TagsDisplay => HasTags
        ? string.Join("   ", Document.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => $"#{tag.Trim()}"))
        : string.Empty;

    public bool HasSchedule => Document.Schedules?.Any() == true;

    public bool IsAiGenerated => !string.IsNullOrWhiteSpace(Document.AiModelDisplayName);

    public string AiGeneratedTooltip => IsAiGenerated
        ? string.Format(LocalizationService.GetString("QuizGeneratedWithModel"), Document.AiModelDisplayName.Trim())
        : string.Empty;

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(QuestionCount));
        OnPropertyChanged(nameof(QuestionCountText));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsDisplay));
        OnPropertyChanged(nameof(HasSchedule));
        OnPropertyChanged(nameof(IsAiGenerated));
        OnPropertyChanged(nameof(AiGeneratedTooltip));
    }
}
