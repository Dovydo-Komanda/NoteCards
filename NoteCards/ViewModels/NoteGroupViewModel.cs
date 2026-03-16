using System.Collections.ObjectModel;
using System.Windows.Media;

namespace NoteCards.ViewModels;

public class NoteGroupViewModel : ViewModelBase
{
    private string _name;
    private Brush _backgroundBrush = Brushes.White;

    public NoteGroupViewModel(Guid groupId, string name, string backgroundColor, IEnumerable<NoteCardViewModel> notes)
    {
        GroupId = groupId;
        _name = name;
        BackgroundColor = backgroundColor;
        _backgroundBrush = CreateBrush(backgroundColor);
        Notes = new ObservableCollection<NoteCardViewModel>(notes);
    }

    public Guid GroupId { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string BackgroundColor { get; private set; }

    public Brush BackgroundBrush
    {
        get => _backgroundBrush;
        private set => SetProperty(ref _backgroundBrush, value);
    }

    public ObservableCollection<NoteCardViewModel> Notes { get; }

    public void SetBackground(string backgroundColor)
    {
        BackgroundColor = backgroundColor;
        BackgroundBrush = CreateBrush(backgroundColor);
    }

    private static Brush CreateBrush(string backgroundColor)
    {
        if (ColorConverter.ConvertFromString(backgroundColor) is Color color)
            return new SolidColorBrush(color);

        return Brushes.White;
    }
}
