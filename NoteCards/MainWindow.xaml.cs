using NoteCards.ViewModels;
using NoteCards.Localization;
using NoteCards.Views;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NoteCards
{
    public partial class MainWindow : Window
    {
        private const double DragScrollEdgeThreshold = 64;
        private const double DragScrollStep = 18;
        private const int SectionAnimationMs = 280;

        private MainViewModel? _observedViewModel;
        private bool _lastKnownGroupsFirst = true;

        private FrameworkElement? RecentSectionBodyElement => FindName("RecentSectionBody") as FrameworkElement;
        private FrameworkElement? GroupsSectionBodyElement => FindName("GroupsSectionBody") as FrameworkElement;
        private ItemsControl? GroupsItemsControlElement => FindName("GroupsItemsControl") as ItemsControl;
        private FrameworkElement? UngroupedSectionBodyElement => FindName("UngroupedSectionBody") as FrameworkElement;
        private FrameworkElement? GroupsSectionContainerElement => FindName("GroupsSectionContainer") as FrameworkElement;
        private FrameworkElement? UngroupedSectionContainerElement => FindName("UngroupedSectionContainer") as FrameworkElement;

        internal static bool IsNoteDragInProgress { get; private set; }

        internal static void SetNoteDragInProgress(bool isInProgress)
        {
            IsNoteDragInProgress = isInProgress;
        }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Unloaded += MainWindow_Unloaded;
            DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AttachViewModel(DataContext as MainViewModel);
            ApplySectionStateImmediately();
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            AttachViewModel(null);
        }

        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            AttachViewModel(e.NewValue as MainViewModel);
            ApplySectionStateImmediately();
        }

        private void AttachViewModel(MainViewModel? vm)
        {
            if (_observedViewModel != null)
                _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;

            _observedViewModel = vm;

            if (_observedViewModel != null)
            {
                _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
                _lastKnownGroupsFirst = _observedViewModel.IsGroupsFirst;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MainViewModel vm)
                return;

            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsRecentSectionExpanded))
                    AnimateSectionVisibility(RecentSectionBodyElement, vm.IsRecentSectionExpanded);
                else if (e.PropertyName == nameof(MainViewModel.IsGroupsSectionExpanded))
                    AnimateSectionVisibility(GroupsSectionBodyElement, vm.IsGroupsSectionExpanded);
                else if (e.PropertyName == nameof(MainViewModel.IsUngroupedSectionExpanded))
                    AnimateSectionVisibility(UngroupedSectionBodyElement, vm.IsUngroupedSectionExpanded);
                else if (e.PropertyName == nameof(MainViewModel.IsGroupsFirst))
                {
                    var movedUp = vm.IsGroupsFirst != _lastKnownGroupsFirst && vm.IsGroupsFirst;
                    var movedDown = vm.IsGroupsFirst != _lastKnownGroupsFirst && !vm.IsGroupsFirst;

                    if (movedUp)
                        AnimateSectionSwap(-20, 20);
                    else if (movedDown)
                        AnimateSectionSwap(20, -20);

                    _lastKnownGroupsFirst = vm.IsGroupsFirst;
                }
                else if (e.PropertyName == nameof(MainViewModel.HasGroups))
                {
                    EnsureExpandedSectionsNotClipped(vm);
                }
            });
        }

        private void ApplySectionStateImmediately()
        {
            if (DataContext is not MainViewModel vm)
                return;

            SetSectionVisibilityImmediately(RecentSectionBodyElement, vm.IsRecentSectionExpanded);
            SetSectionVisibilityImmediately(GroupsSectionBodyElement, vm.IsGroupsSectionExpanded);
            SetSectionVisibilityImmediately(UngroupedSectionBodyElement, vm.IsUngroupedSectionExpanded);
            EnsureExpandedSectionsNotClipped(vm);
            _lastKnownGroupsFirst = vm.IsGroupsFirst;
        }

        private static void EnsureExpandedSectionsNotClipped(MainViewModel vm)
        {
            if (Application.Current?.MainWindow is not MainWindow window)
                return;

            if (vm.IsRecentSectionExpanded && window.RecentSectionBodyElement is FrameworkElement recent)
                recent.MaxHeight = double.PositiveInfinity;

            if (vm.IsGroupsSectionExpanded && window.GroupsSectionBodyElement is FrameworkElement groups)
                groups.MaxHeight = double.PositiveInfinity;

            if (vm.IsUngroupedSectionExpanded && window.UngroupedSectionBodyElement is FrameworkElement ungrouped)
                ungrouped.MaxHeight = double.PositiveInfinity;
        }

        private static void SetSectionVisibilityImmediately(FrameworkElement? section, bool isExpanded)
        {
            if (section is null)
                return;

            section.ClipToBounds = true;
            var translate = EnsureSectionTranslateTransform(section);

            if (isExpanded)
            {
                section.Visibility = Visibility.Visible;
                section.Opacity = 1;
                section.MaxHeight = double.PositiveInfinity;
                translate.Y = 0;
                return;
            }

            section.Visibility = Visibility.Collapsed;
            section.Opacity = 0;
            section.MaxHeight = 0;
            translate.Y = -8;
        }

        private static void AnimateSectionVisibility(FrameworkElement? section, bool isExpanded)
        {
            if (section is null)
                return;

            section.BeginAnimation(OpacityProperty, null);
            section.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            var translate = EnsureSectionTranslateTransform(section);
            translate.BeginAnimation(TranslateTransform.YProperty, null);

            var duration = TimeSpan.FromMilliseconds(SectionAnimationMs);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            if (isExpanded)
            {
                section.Visibility = Visibility.Visible;
                section.ClipToBounds = true;
                section.MaxHeight = double.PositiveInfinity;

                section.Measure(new Size(section.ActualWidth > 0 ? section.ActualWidth : double.PositiveInfinity, double.PositiveInfinity));
                var targetHeight = Math.Max(1, section.DesiredSize.Height + 8);

                section.MaxHeight = 0;
                section.Opacity = 0;
                translate.Y = -8;

                var expandHeight = new DoubleAnimation(0, targetHeight, duration) { EasingFunction = ease };
                expandHeight.Completed += (_, _) => section.MaxHeight = double.PositiveInfinity;

                section.BeginAnimation(FrameworkElement.MaxHeightProperty, expandHeight);
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, duration) { EasingFunction = ease });
                section.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                return;
            }

            var startHeight = section.ActualHeight;
            if (startHeight <= 0)
            {
                section.Visibility = Visibility.Collapsed;
                section.Opacity = 0;
                section.MaxHeight = 0;
                translate.Y = -8;
                return;
            }

            section.MaxHeight = startHeight;
            var collapseAnimation = new DoubleAnimation(translate.Y, -8, duration) { EasingFunction = ease };
            collapseAnimation.Completed += (_, _) =>
            {
                section.Visibility = Visibility.Collapsed;
                section.Opacity = 0;
                section.MaxHeight = 0;
                translate.Y = -8;
            };

            section.BeginAnimation(FrameworkElement.MaxHeightProperty, new DoubleAnimation(startHeight, 0, duration)
            {
                EasingFunction = ease
            });
            translate.BeginAnimation(TranslateTransform.YProperty, collapseAnimation);
            section.BeginAnimation(OpacityProperty, new DoubleAnimation(section.Opacity, 0, TimeSpan.FromMilliseconds(180)));
        }

        private static TranslateTransform EnsureSectionTranslateTransform(FrameworkElement section)
        {
            section.RenderTransformOrigin = new Point(0.5, 0);

            if (section.RenderTransform is TransformGroup group)
            {
                var existingTranslate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (existingTranslate != null)
                    return existingTranslate;

                var translate = new TranslateTransform(0, 0);
                group.Children.Insert(0, translate);
                return translate;
            }

            if (section.RenderTransform is TranslateTransform directTranslate)
                return directTranslate;

            var transformGroup = new TransformGroup();
            var newTranslate = new TranslateTransform(0, 0);
            transformGroup.Children.Add(newTranslate);

            if (section.RenderTransform != null && section.RenderTransform != Transform.Identity)
                transformGroup.Children.Add(section.RenderTransform);

            section.RenderTransform = transformGroup;
            return newTranslate;
        }

        private void AnimateSectionSwap(double groupsOffset, double ungroupedOffset)
        {
            AnimateSectionReflow(GroupsSectionContainerElement, groupsOffset);
            AnimateSectionReflow(UngroupedSectionContainerElement, ungroupedOffset);
        }

        private static void AnimateSectionReflow(FrameworkElement? element, double startOffset)
        {
            if (element is null)
                return;

            if (element.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                element.RenderTransform = transform;
            }

            transform.BeginAnimation(TranslateTransform.YProperty, null);
            element.BeginAnimation(OpacityProperty, null);

            transform.Y = startOffset;
            element.Opacity = 0.82;

            var duration = TimeSpan.FromMilliseconds(SectionAnimationMs);
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startOffset, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.82, 1, duration));
        }

        private void NotesScrollViewer_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!IsNoteDragInProgress || sender is not ScrollViewer scrollViewer)
                return;

            if (e.Data.GetData(typeof(NoteCardViewModel)) is not NoteCardViewModel)
                return;

            var cursorPosition = e.GetPosition(scrollViewer);

            if (cursorPosition.Y < DragScrollEdgeThreshold)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - DragScrollStep));
            }
            else if (cursorPosition.Y > scrollViewer.ViewportHeight - DragScrollEdgeThreshold)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + DragScrollStep));
            }
        }

        private void NotesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!IsNoteDragInProgress || sender is not ScrollViewer scrollViewer)
                return;

            var nextOffset = Math.Clamp(scrollViewer.VerticalOffset - (e.Delta / 3d), 0, scrollViewer.ScrollableHeight);
            scrollViewer.ScrollToVerticalOffset(nextOffset);
            e.Handled = true;
        }

        private void RecentNotesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            var nextOffset = Math.Clamp(scrollViewer.HorizontalOffset - (e.Delta / 3d), 0, scrollViewer.ScrollableWidth);
            scrollViewer.ScrollToHorizontalOffset(nextOffset);
            e.Handled = true;
        }

        private void OpenFromFileMenuButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = false;

            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = LocalizationService.GetString("OpenFileDialogFilter");
            if (dlg.ShowDialog() != true)
                return;

            var path = dlg.FileName;
            try
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                string content = string.Empty;

                if (ext == ".rtf")
                {
                    var bytes = System.IO.File.ReadAllBytes(path);
                    content = Convert.ToBase64String(bytes);
                }
                else
                {
                    // Try strict UTF8 then fallbacks
                    var rawBytes = System.IO.File.ReadAllBytes(path);
                    try
                    {
                        content = new System.Text.UTF8Encoding(false, true).GetString(rawBytes);
                    }
                    catch
                    {
                        try
                        {
                            using (var ms = new System.IO.MemoryStream(rawBytes))
                            using (var sr = new System.IO.StreamReader(ms, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                            {
                                content = sr.ReadToEnd();
                            }
                        }
                        catch
                        {
                            try { content = System.Text.Encoding.Default.GetString(rawBytes); }
                            catch { try { content = System.Text.Encoding.GetEncoding(1257).GetString(rawBytes); } catch { content = string.Empty; } }
                        }
                    }
                }

                var vm = this.DataContext as MainViewModel;
                if (vm == null) return;

                // Try find existing note with identical content; otherwise create new
                NoteCardViewModel? existing = null;
                foreach (var n in vm.Notes)
                {
                    if (n.Document.Content == content)
                    {
                        existing = n; break;
                    }
                }

                NoteCardViewModel target;
                if (existing != null)
                {
                    target = existing;
                }
                else
                {
                    var doc = new NoteCards.Models.NoteDocument
                    {
                        Title = System.IO.Path.GetFileNameWithoutExtension(path),
                        Content = content
                    };
                    target = vm.AddNoteFromDocument(doc);
                }

                OpenNoteEditor(target);
            }
            catch
            {
                MessageBox.Show(LocalizationService.GetString("FailedToOpenFile"), LocalizationService.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = !HamburgerPopup.IsOpen;
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = false;
            var about = new Views.AboutWindow { Owner = this };
            about.ShowDialog();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.MainViewModel vm)
            {
                vm.SearchQuery = string.Empty;
            }
        }

        // Open editor for a specific note card
        public void OpenNoteEditor(NoteCardViewModel noteViewModel)
        {
            var editor = new NoteEditorWindow();
            // Set DataContext so EnableScrollbar binding works
            editor.DataContext = this.DataContext;

            editor.LoadFromDocument(noteViewModel.Document);

            if (editor.ShowDialog() == true)
            {
                editor.SaveToDocument(noteViewModel.Document);

                // Notify the card's bindings to refresh with the new content/title
                noteViewModel.NotifyContentChanged();

                var vm = this.DataContext as MainViewModel;
                vm?.RefreshRecentNotes();
                vm?.SaveNotes();
            }
        }

        // Settings menu button click handler
        private void SettingsMenuButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = false;
            var settingsPanel = this.FindName("SettingsPanelControl") as FrameworkElement;
            if (settingsPanel != null)
            {
                settingsPanel.DataContext = this.DataContext;
                settingsPanel.Visibility = Visibility.Visible;
            }
        }
        private void RecentNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NoteCardViewModel noteVm)
                OpenNoteEditor(noteVm);
        }

        private void ToggleRecentSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsRecentSectionExpanded = !vm.IsRecentSectionExpanded;
        }

        private void ToggleGroupsSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsGroupsSectionExpanded = !vm.IsGroupsSectionExpanded;
        }

        private void ToggleUngroupedSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsUngroupedSectionExpanded = !vm.IsUngroupedSectionExpanded;
        }

        private void MoveGroupsUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsGroupsFirst = true;
        }

        private void MoveGroupsDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsGroupsFirst = false;
        }

        private void MoveUngroupedUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsGroupsFirst = false;
        }

        private void MoveUngroupedDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsGroupsFirst = true;
        }

        private void MoveSingleGroupUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: NoteGroupViewModel group })
                return;

            if (DataContext is MainViewModel vm)
            {
                var currentIndex = vm.NoteGroups.IndexOf(group);
                var swapGroupId = currentIndex > 0 ? vm.NoteGroups[currentIndex - 1].GroupId : Guid.Empty;

                if (vm.MoveGroupUp(group))
                    AnimateGroupOrderChange(group.GroupId, swapGroupId, -14, 14);
            }
        }

        private void MoveSingleGroupDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: NoteGroupViewModel group })
                return;

            if (DataContext is MainViewModel vm)
            {
                var currentIndex = vm.NoteGroups.IndexOf(group);
                var swapGroupId = currentIndex >= 0 && currentIndex < vm.NoteGroups.Count - 1
                    ? vm.NoteGroups[currentIndex + 1].GroupId
                    : Guid.Empty;

                if (vm.MoveGroupDown(group))
                    AnimateGroupOrderChange(group.GroupId, swapGroupId, 14, -14);
            }
        }

        private void AnimateGroupOrderChange(Guid primaryGroupId, Guid secondaryGroupId, double primaryOffset, double secondaryOffset)
        {
            var groupsList = GroupsItemsControlElement;
            if (groupsList is null)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is not MainViewModel vm)
                    return;

                var duration = TimeSpan.FromMilliseconds(130);
                var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

                var first = vm.NoteGroups.FirstOrDefault(g => g.GroupId == primaryGroupId);
                if (first != null)
                    AnimateGroupContainer(groupsList, first, primaryOffset, duration, easing);

                if (secondaryGroupId != Guid.Empty)
                {
                    var second = vm.NoteGroups.FirstOrDefault(g => g.GroupId == secondaryGroupId);
                    if (second != null)
                        AnimateGroupContainer(groupsList, second, secondaryOffset, duration, easing);
                }
            }, DispatcherPriority.Render);
        }

        private static void AnimateGroupContainer(ItemsControl groupsList, object item, double offset, Duration duration, IEasingFunction easing)
        {
            if (groupsList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container)
                return;

            TranslateTransform translate;
            if (container.RenderTransform is TranslateTransform direct)
            {
                translate = direct;
            }
            else
            {
                translate = new TranslateTransform();
                container.RenderTransform = translate;
            }

            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = offset;
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, duration)
            {
                EasingFunction = easing
            });
        }

        private void GroupBorder_DragOver(object sender, DragEventArgs e)
        {
            var draggedNote = e.Data.GetData(typeof(NoteCardViewModel)) as NoteCardViewModel;
            if (draggedNote is null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void GroupBorder_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Border border || border.Tag is not NoteGroupViewModel targetGroup)
                return;

            var draggedNote = e.Data.GetData(typeof(NoteCardViewModel)) as NoteCardViewModel;
            if (draggedNote is null)
                return;

            if (DataContext is MainViewModel vm)
                vm.TryMoveNoteToGroup(draggedNote, targetGroup);

            e.Handled = true;
        }

        private void UngroupedDropZone_DragOver(object sender, DragEventArgs e)
        {
            var draggedNote = e.Data.GetData(typeof(NoteCardViewModel)) as NoteCardViewModel;
            if (draggedNote?.IsGrouped == true)
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void UngroupedDropZone_Drop(object sender, DragEventArgs e)
        {
            var draggedNote = e.Data.GetData(typeof(NoteCardViewModel)) as NoteCardViewModel;
            if (draggedNote is null)
                return;

            if (DataContext is MainViewModel vm)
                vm.TryDropToUngrouped(draggedNote);

            e.Handled = true;
        }

        private void GroupMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void GroupColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void RenameGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var group = ResolveGroupFromMenuSender(sender);
            if (group is null || DataContext is not MainViewModel vm)
                return;

            var dialog = new SimpleInputDialog(
                LocalizationService.GetString("RenameGroup"),
                LocalizationService.GetString("RenameGroupPrompt"),
                group.Name)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
                vm.RenameGroup(group, dialog.InputText);
        }

        private void GroupColorMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string colorHex)
                return;

            var group = ResolveGroupFromMenuSender(sender);
            if (group is null || DataContext is not MainViewModel vm)
                return;

            vm.SetGroupBackgroundColor(group, colorHex);
        }

        private void DisbandGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var group = ResolveGroupFromMenuSender(sender);
            if (group is null || DataContext is not MainViewModel vm)
                return;

            var result = MessageBox.Show(
                LocalizationService.GetString("DisbandGroupPrompt"),
                LocalizationService.GetString("DisbandGroup"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
                return;

            var keepNotesUngrouped = result == MessageBoxResult.Yes;
            vm.DisbandGroup(group, deleteNotes: !keepNotesUngrouped);
        }

        private static NoteGroupViewModel? ResolveGroupFromMenuSender(object sender)
        {
            if (sender is not MenuItem menuItem)
                return null;

            DependencyObject? current = menuItem;
            ContextMenu? contextMenu = null;

            while (current != null)
            {
                if (current is ContextMenu cm)
                {
                    contextMenu = cm;
                    break;
                }

                current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
            }

            if (contextMenu?.PlacementTarget is not FrameworkElement placementTarget)
                return null;

            return placementTarget.DataContext as NoteGroupViewModel
                ?? (placementTarget as Button)?.Tag as NoteGroupViewModel;
        }
    }
}