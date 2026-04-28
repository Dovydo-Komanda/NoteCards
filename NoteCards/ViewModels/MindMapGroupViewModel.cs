using System.Collections.ObjectModel;

namespace NoteCards.ViewModels;

public class MindMapGroupViewModel : ViewModelBase
{
    private string _name;

    public MindMapGroupViewModel(Guid groupId, string name, IEnumerable<MindMapViewModel> maps)
    {
        GroupId = groupId;
        _name = name;
        MindMaps = new ObservableCollection<MindMapViewModel>(maps);
    }

    public Guid GroupId { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public ObservableCollection<MindMapViewModel> MindMaps { get; }
}