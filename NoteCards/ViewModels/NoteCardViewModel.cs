using System.Windows.Input;
using NoteCards.Models;

namespace NoteCards.ViewModels;

public class NoteCardViewModel : ViewModelBase
{
    private bool _isMenuVisible;

    public NoteCardViewModel(NoteDocument document, Action<NoteCardViewModel> deleteAction)
    {
        Document = document;
        DeleteCommand = new RelayCommand(() => deleteAction(this));
    }

    public NoteDocument Document { get; }

    public string Title => Document.Title;
    public string Content => Document.Content;

    public bool IsMenuVisible
    {
        get => _isMenuVisible;
        set => SetProperty(ref _isMenuVisible, value);
    }

    public ICommand DeleteCommand { get; }
}
