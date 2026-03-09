using System.Windows.Input;
using NoteCards.Models;

namespace NoteCards.ViewModels;

public class NoteCardViewModel : ViewModelBase
{
    private bool _isMenuVisible;
    private bool _isDeleting;
    private readonly Action<NoteCardViewModel> _deleteAction;

    public NoteCardViewModel(NoteDocument document, Action<NoteCardViewModel> deleteAction)
    {
        Document = document;
        _deleteAction = deleteAction;
        DeleteCommand = new RelayCommand(() => _deleteAction?.Invoke(this));
    }

    public NoteDocument Document { get; }

    public string Title => Document.Title;
    public string Content => Document.Content;

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

    public ICommand DeleteCommand { get; }

    internal void ExecuteDelete() => _deleteAction(this);
}
