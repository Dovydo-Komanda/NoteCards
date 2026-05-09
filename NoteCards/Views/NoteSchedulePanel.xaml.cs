using NoteCards.Models;
using NoteCards.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views;

public partial class NoteSchedulePanel : UserControl
{
    private const int OverlayAnimationMs = 180;
    private const int PanelAnimationMs = 220;
    private const double PanelOffsetY = 14;

    private bool _isClosing;
    private MainViewModel? _mainViewModel;
    private NoteCardViewModel? _selectedNote;
    private Action<IEnumerable<NoteScheduleEntry>>? _saveCallback;

    public ObservableCollection<NoteScheduleEntry> ScheduleItems { get; } = new();

    public NoteSchedulePanel()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void ShowAnimated(MainViewModel viewModel, NoteCardViewModel note)
    {
        _mainViewModel = viewModel;
        _selectedNote = note;
        _saveCallback = null;
        ScheduleTitleText.Text = NoteCards.Localization.LocalizationService.GetString("CalendarAssignTitle");
        ScheduleSubtitleText.Text = note.Document.Title ?? string.Empty;
        ScheduleSubtitleText.Visibility = string.IsNullOrEmpty(ScheduleSubtitleText.Text) ? Visibility.Collapsed : Visibility.Visible;

        ScheduleItems.Clear();
        foreach (var schedule in BuildWorkingSchedules(note))
            ScheduleItems.Add(schedule);

        PopulateTimeSelectors();
        SetEntryEditorsFromNow();

        _isClosing = false;
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;

        OverlayRoot.BeginAnimation(OpacityProperty, null);
        PanelCard.BeginAnimation(OpacityProperty, null);
        var translate = EnsurePanelTranslate();
        translate.BeginAnimation(TranslateTransform.YProperty, null);

        OverlayRoot.Opacity = 0;
        PanelCard.Opacity = 0;
        translate.Y = PanelOffsetY;

        var overlayDuration = TimeSpan.FromMilliseconds(OverlayAnimationMs);
        var panelDuration = TimeSpan.FromMilliseconds(PanelAnimationMs);
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        OverlayRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, overlayDuration) { EasingFunction = easeOut });
        PanelCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, panelDuration) { EasingFunction = easeOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(PanelOffsetY, 0, panelDuration) { EasingFunction = easeOut });
    }

    public void ShowAnimated(MainViewModel viewModel, IEnumerable<NoteScheduleEntry> existingSchedules, Action<IEnumerable<NoteScheduleEntry>> onSave, string title, string subtitle = "")
    {
        _mainViewModel = viewModel;
        _selectedNote = null;
        _saveCallback = onSave;
        ScheduleTitleText.Text = title;
        ScheduleSubtitleText.Text = subtitle;
        ScheduleSubtitleText.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;

        ScheduleItems.Clear();
        var sorted = existingSchedules
            .Select(e => new NoteScheduleEntry { ScheduledAt = e.ScheduledAt, Note = e.Note ?? string.Empty })
            .OrderBy(e => e.ScheduledAt)
            .ThenBy(e => e.Note, StringComparer.CurrentCultureIgnoreCase);
        foreach (var schedule in sorted)
            ScheduleItems.Add(schedule);

        PopulateTimeSelectors();
        SetEntryEditorsFromNow();

        _isClosing = false;
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;

        OverlayRoot.BeginAnimation(OpacityProperty, null);
        PanelCard.BeginAnimation(OpacityProperty, null);
        var translate = EnsurePanelTranslate();
        translate.BeginAnimation(TranslateTransform.YProperty, null);

        OverlayRoot.Opacity = 0;
        PanelCard.Opacity = 0;
        translate.Y = PanelOffsetY;

        var overlayDuration = TimeSpan.FromMilliseconds(OverlayAnimationMs);
        var panelDuration = TimeSpan.FromMilliseconds(PanelAnimationMs);
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        OverlayRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, overlayDuration) { EasingFunction = easeOut });
        PanelCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, panelDuration) { EasingFunction = easeOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(PanelOffsetY, 0, panelDuration) { EasingFunction = easeOut });
    }

    private static List<NoteScheduleEntry> BuildWorkingSchedules(NoteCardViewModel note)
    {
        var schedules = note.Document.Schedules?.ToList() ?? new List<NoteScheduleEntry>();
        if (schedules.Count == 0 && note.Document.ScheduledAt.HasValue)
        {
            schedules.Add(new NoteScheduleEntry
            {
                ScheduledAt = note.Document.ScheduledAt.Value,
                Note = note.Document.ScheduleNote ?? string.Empty
            });
        }

        return schedules
            .Select(entry => new NoteScheduleEntry
            {
                ScheduledAt = entry.ScheduledAt,
                Note = entry.Note ?? string.Empty
            })
            .OrderBy(entry => entry.ScheduledAt)
            .ThenBy(entry => entry.Note, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void PopulateTimeSelectors()
    {
        if (HourComboBox.Items.Count == 24)
            return;

        HourComboBox.Items.Clear();
        MinuteComboBox.Items.Clear();

        for (var hour = 0; hour < 24; hour++)
            HourComboBox.Items.Add(hour.ToString("00"));

        for (var minute = 0; minute < 60; minute++)
            MinuteComboBox.Items.Add(minute.ToString("00"));
    }

    private void SetEntryEditorsFromNow()
    {
        var now = DateTime.Now;
        ScheduleDatePicker.SelectedDate = now.Date;
        HourComboBox.SelectedItem = now.Hour.ToString("00");
        MinuteComboBox.Text = now.Minute.ToString("00");
        ScheduleNoteTextBox.Text = string.Empty;
    }

    private void AddScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ScheduleDatePicker.SelectedDate.HasValue)
            return;

        var hour = HourComboBox.SelectedItem?.ToString() ?? "00";
        var minute = string.IsNullOrWhiteSpace(MinuteComboBox.Text)
            ? MinuteComboBox.SelectedItem?.ToString() ?? "00"
            : MinuteComboBox.Text;

        if (!int.TryParse(hour, out var selectedHour))
            selectedHour = 0;
        if (!int.TryParse(minute, out var selectedMinute))
            selectedMinute = 0;

        selectedMinute = Math.Clamp(selectedMinute, 0, 59);

        var entry = new NoteScheduleEntry
        {
            ScheduledAt = ScheduleDatePicker.SelectedDate.Value.Date.AddHours(selectedHour).AddMinutes(selectedMinute),
            Note = (ScheduleNoteTextBox.Text ?? string.Empty).Trim()
        };

        ScheduleItems.Add(entry);
        SortScheduleItems();
        SetEntryEditorsFromNow();
    }

    private void RemoveScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: NoteScheduleEntry entry })
            return;

        ScheduleItems.Remove(entry);
    }

    private void SortScheduleItems()
    {
        var ordered = ScheduleItems
            .OrderBy(item => item.ScheduledAt)
            .ThenBy(item => item.Note, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ScheduleItems.Clear();
        foreach (var item in ordered)
            ScheduleItems.Add(item);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_saveCallback != null)
            _saveCallback(ScheduleItems);
        else if (_mainViewModel != null && _selectedNote != null)
            _mainViewModel.SetNoteSchedules(_selectedNote, ScheduleItems);

        _saveCallback = null;
        HideAnimated();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ScheduleItems.Clear();
        if (_saveCallback != null)
            _saveCallback(ScheduleItems);
        else if (_mainViewModel != null && _selectedNote != null)
            _mainViewModel.SetNoteSchedules(_selectedNote, ScheduleItems);

        _saveCallback = null;
        HideAnimated();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideAnimated();
    }

    public void HideAnimated()
    {
        if (_isClosing || Visibility != Visibility.Visible)
            return;

        _isClosing = true;
        IsHitTestVisible = false;

        var translate = EnsurePanelTranslate();
        var startOverlayOpacity = OverlayRoot.Opacity <= 0 ? 1 : OverlayRoot.Opacity;
        var startPanelOpacity = PanelCard.Opacity <= 0 ? 1 : PanelCard.Opacity;

        var overlayDuration = TimeSpan.FromMilliseconds(OverlayAnimationMs);
        var panelDuration = TimeSpan.FromMilliseconds(PanelAnimationMs);
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        OverlayRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(startOverlayOpacity, 0, overlayDuration) { EasingFunction = easeIn });
        PanelCard.BeginAnimation(OpacityProperty, new DoubleAnimation(startPanelOpacity, 0, panelDuration) { EasingFunction = easeIn });

        var closePanelShift = new DoubleAnimation(translate.Y, PanelOffsetY, panelDuration) { EasingFunction = easeIn };
        closePanelShift.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            OverlayRoot.Opacity = 0;
            PanelCard.Opacity = 0;
            translate.Y = PanelOffsetY;
            _isClosing = false;
        };

        translate.BeginAnimation(TranslateTransform.YProperty, closePanelShift);
    }

    private TranslateTransform EnsurePanelTranslate()
    {
        if (PanelCard.RenderTransform is TranslateTransform translate)
            return translate;

        translate = new TranslateTransform();
        PanelCard.RenderTransform = translate;
        return translate;
    }

    private void OverlayRoot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
            HideAnimated();
    }
}
