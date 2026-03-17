using NoteCards.Models;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace NoteCards.ViewModels;

public class NoteCardViewModel : ViewModelBase
{
    private bool _isMenuVisible;
    private bool _isDeleting;
    private readonly Action<NoteCardViewModel> _deleteAction;
    private readonly Action<NoteCardViewModel>? _removeFromGroupAction;

    public NoteCardViewModel(
        NoteDocument document,
        Action<NoteCardViewModel> deleteAction,
        Action<NoteCardViewModel>? removeFromGroupAction = null)
    {
        Document = document;
        _deleteAction = deleteAction;
        _removeFromGroupAction = removeFromGroupAction;
        DeleteCommand = new RelayCommand(() => IsDeleting = true);
        RemoveFromGroupCommand = new RelayCommand(RemoveFromGroup, () => IsGrouped);
    }

    public bool HasTags => Document.Tags?.Count > 0;

    public string TagsDisplay
    {
        get
        {
            if (!HasTags)
                return string.Empty;

            return string.Join("   ", Document.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => $"-{tag.Trim()}"));
        }
    }

    public string TagsSearchText
    {
        get
        {
            if (!HasTags)
                return string.Empty;

            return string.Join(" ", Document.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim()));
        }
    }

    public NoteDocument Document { get; }

    public string Title => Document.Title;
    // Return a plain-text preview for the card. The stored Document.Content
    // may be plain text or Base64-encoded RTF (saved by the editor). If it's
    // RTF we decode and extract the text so the card shows readable content
    // instead of raw Base64/RTF markup.
    public string Content
    {
        get
        {
            var raw = Document.Content;
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            try
            {
                // Try to treat stored content as Base64-encoded RTF
                byte[] bytes = Convert.FromBase64String(raw);

                // Heuristic: ensure decoded bytes start with RTF header
                if (bytes.Length >= 5)
                {
                    var hdr = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(5, bytes.Length));
                    if (!hdr.StartsWith("{\\rtf"))
                        throw new FormatException();
                }

                using (var ms = new MemoryStream(bytes))
                {
                    var flowDoc = new FlowDocument();
                    var tr = new TextRange(flowDoc.ContentStart, flowDoc.ContentEnd);
                    tr.Load(ms, DataFormats.Rtf);
                    // Re-create the range after loading so it spans the full inserted content
                    return new TextRange(flowDoc.ContentStart, flowDoc.ContentEnd).Text?.Trim() ?? string.Empty;
                }
            }
            catch (FormatException)
            {
                // Not Base64 -> assume plain text
                return raw;
            }
            catch
            {
                // Any other failure (e.g. malformed RTF): return empty rather than raw Base64 garbage
                return string.Empty;
            }
        }
    }

    public bool IsMenuVisible
    {
        get => _isMenuVisible;
        set => SetProperty(ref _isMenuVisible, value);
    }

    public bool IsDeleting
    {
        get => _isDeleting;
        set => SetProperty(ref _isDeleting, value);
    }

    public bool IsGrouped => Document.GroupId.HasValue;

    public ICommand DeleteCommand { get; }

    public ICommand RemoveFromGroupCommand { get; }

    internal void ExecuteDelete() => _deleteAction(this);

    public void NotifyContentChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsDisplay));
        OnPropertyChanged(nameof(TagsSearchText));
        OnPropertyChanged(nameof(IsGrouped));
        CommandManager.InvalidateRequerySuggested();
    }

    public void NotifyGroupChanged()
    {
        OnPropertyChanged(nameof(IsGrouped));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemoveFromGroup()
    {
        _removeFromGroupAction?.Invoke(this);
    }
}
