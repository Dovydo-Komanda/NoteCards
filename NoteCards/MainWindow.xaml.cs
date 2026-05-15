using NoteCards.Animations;
using NoteCards.Localization;
using NoteCards.Models;
using NoteCards.Services;
using NoteCards.ViewModels;
using NoteCards.Views;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Markup;

namespace NoteCards
{
    public partial class MainWindow : Window
    {
        private const double DragScrollEdgeThreshold = 64;
        private const double DragScrollStep = 18;
        private const double DashboardDragScrollEdgeThreshold = 96;
        private const double DashboardDragScrollMinStep = 16;
        private const double DashboardDragScrollMaxStep = 46;
        private const int SectionAnimationMs = 280;
        private const int DashboardViewAnimationMs = 420;
        private const double TopSearchExpandedWidth = 320;
        private const int TopSearchAnimationMs = 300;
        private const int SidebarAnimationMs = 240;

        private MainViewModel? _observedViewModel;
        private bool _lastKnownGroupsFirst = true;
        private bool _notesLayoutRefreshQueued;
        private NoteEditorTabsWindow? _noteEditorTabsWindow;
        private Point _dashboardListingDragStart;
        private bool _suppressDashboardListingOpen;
        private readonly Dictionary<Border, Brush?> _dashboardDropOriginalBorderBrushes = new();
        private readonly Dictionary<Border, Thickness> _dashboardDropOriginalBorderThicknesses = new();
        private DispatcherTimer? _dashboardDragScrollTimer;
        private ScrollViewer? _dashboardDragScrollViewer;
        private Point _dashboardDragScrollPosition;

        private FrameworkElement? RecentSectionBodyElement => FindName("RecentSectionBody") as FrameworkElement;
        private FrameworkElement? FlashcardRecentSectionBodyElement => FindName("FlashcardRecentSectionBody") as FrameworkElement;
        private FrameworkElement? QuizRecentSectionBodyElement => FindName("QuizRecentSectionBody") as FrameworkElement;
        private FrameworkElement? MindMapRecentSectionBodyElement => FindName("MindMapRecentSectionBody") as FrameworkElement;
        private FrameworkElement? CalendarSectionBodyElement => FindName("CalendarSectionBody") as FrameworkElement;
        private FrameworkElement? FlashcardCalendarSectionBodyElement => FindName("FlashcardCalendarSectionBody") as FrameworkElement;
        private FrameworkElement? QuizCalendarSectionBodyElement => FindName("QuizCalendarSectionBody") as FrameworkElement;
        private FrameworkElement? MindMapCalendarSectionBodyElement => FindName("MindMapCalendarSectionBody") as FrameworkElement;
        private FrameworkElement? GroupsSectionBodyElement => FindName("GroupsSectionBody") as FrameworkElement;
        private ItemsControl? GroupsItemsControlElement => FindName("GroupsItemsControl") as ItemsControl;
        private FrameworkElement? UngroupedSectionBodyElement => FindName("UngroupedSectionBody") as FrameworkElement;
        private FrameworkElement? CalendarSectionContainerElement => FindName("CalendarSectionContainer") as FrameworkElement;
        private FrameworkElement? GroupsSectionContainerElement => FindName("GroupsSectionContainer") as FrameworkElement;
        private FrameworkElement? UngroupedSectionContainerElement => FindName("UngroupedSectionContainer") as FrameworkElement;
        private FrameworkElement? MassSelectOverlayToolbarElement => FindName("MassSelectOverlayToolbar") as FrameworkElement;
        private FrameworkElement? MassSelectMoreActionsPanelElement => FindName("MassSelectMoreActionsPanel") as FrameworkElement;
        private Button? MassSelectMoreActionsToggleButtonElement => FindName("MassSelectMoreActionsToggleButton") as Button;
        private Popup? SortNotesPopupElement => FindName("SortNotesPopup") as Popup;
        private Popup? DashboardSectionsPopupElement => FindName("DashboardSectionsPopup") as Popup;
        private ColumnDefinition? SidebarColumnElement => FindName("SidebarColumn") as ColumnDefinition;
        private bool _isMassSelectMoreActionsExpanded;

        internal static bool IsNoteDragInProgress { get; private set; }

        internal static void SetNoteDragInProgress(bool isInProgress)
        {
            IsNoteDragInProgress = isInProgress;
        }

        public MainWindow()
        {
            InitializeComponent();
            NoteCards.Services.WindowThemeService.Register(this);
            NoteCards.Services.ActivityTracker.Initialize();
            ApplyCurrentLanguage();
            LocalizationService.CultureChanged += LocalizationService_CultureChanged;
            Loaded += MainWindow_Loaded;
            Unloaded += MainWindow_Unloaded;
            DataContextChanged += MainWindow_DataContextChanged;
        }

        private void LocalizationService_CultureChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(ApplyCurrentLanguage);
        }

        private void ApplyCurrentLanguage()
        {
            Language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AttachViewModel(DataContext as MainViewModel);
            ApplySectionStateImmediately();
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.CultureChanged -= LocalizationService_CultureChanged;
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
            {
                _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _observedViewModel.Notes.CollectionChanged -= ViewModel_NotesCollectionChanged;
                _observedViewModel.NoteGroups.CollectionChanged -= ViewModel_NotesCollectionChanged;
            }

            _observedViewModel = vm;

            if (_observedViewModel != null)
            {
                _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
                _observedViewModel.Notes.CollectionChanged += ViewModel_NotesCollectionChanged;
                _observedViewModel.NoteGroups.CollectionChanged += ViewModel_NotesCollectionChanged;
                _lastKnownGroupsFirst = _observedViewModel.IsGroupsFirst;
                QueueNotesLayoutRefresh();
            }
        }

        private void ViewModel_NotesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            QueueNotesLayoutRefresh();
        }

        private void QueueNotesLayoutRefresh()
        {
            if (_notesLayoutRefreshQueued)
                return;

            _notesLayoutRefreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _notesLayoutRefreshQueued = false;

                NotesScrollViewer?.InvalidateMeasure();
                NotesScrollViewer?.InvalidateArrange();
                CalendarSectionContainerElement?.InvalidateMeasure();
                CalendarSectionBodyElement?.InvalidateMeasure();
                GroupsSectionContainerElement?.InvalidateMeasure();
                GroupsSectionBodyElement?.InvalidateMeasure();
                UngroupedSectionContainerElement?.InvalidateMeasure();
                UngroupedSectionBodyElement?.InvalidateMeasure();

                if (_observedViewModel != null)
                    EnsureExpandedSectionsNotClipped(_observedViewModel);

                NotesScrollViewer?.UpdateLayout();
            }), DispatcherPriority.Render);
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MainViewModel vm)
                return;

            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsRecentSectionExpanded))
                {
                    AnimateSectionVisibility(RecentSectionBodyElement, vm.IsRecentSectionExpanded);
                    AnimateSectionVisibility(FlashcardRecentSectionBodyElement, vm.IsRecentSectionExpanded);
                    AnimateSectionVisibility(QuizRecentSectionBodyElement, vm.IsRecentSectionExpanded);
                    AnimateSectionVisibility(MindMapRecentSectionBodyElement, vm.IsRecentSectionExpanded);
                }
                else if (e.PropertyName == nameof(MainViewModel.IsCalendarSectionExpanded))
                {
                    AnimateSectionVisibility(CalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
                    AnimateSectionVisibility(FlashcardCalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
                    AnimateSectionVisibility(QuizCalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
                    AnimateSectionVisibility(MindMapCalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
                }
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
                else if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded)
                      || e.PropertyName == nameof(MainViewModel.SidebarWidth))
                {
                    AnimateSidebarWidth(vm.SidebarWidth);
                }
                else if (e.PropertyName == nameof(MainViewModel.IsMassSelectMode))
                {
                    AnimateMassSelectOverlay(vm.IsMassSelectMode);
                }
                else if (e.PropertyName == nameof(MainViewModel.IsFlashcardsView)
                      || e.PropertyName == nameof(MainViewModel.IsMindMapsView)
                      || e.PropertyName == nameof(MainViewModel.IsQuizzesView)
                      || e.PropertyName == nameof(MainViewModel.IsNotesView))
                {
                    CloseNotesDashboardChrome();
                }
            });
        }

        private void ApplySectionStateImmediately()
        {
            if (DataContext is not MainViewModel vm)
                return;

            SetSectionVisibilityImmediately(RecentSectionBodyElement, vm.IsRecentSectionExpanded);
            SetSectionVisibilityImmediately(FlashcardRecentSectionBodyElement, vm.IsRecentSectionExpanded);
            SetSectionVisibilityImmediately(QuizRecentSectionBodyElement, vm.IsRecentSectionExpanded);
            SetSectionVisibilityImmediately(MindMapRecentSectionBodyElement, vm.IsRecentSectionExpanded);
            SetSectionVisibilityImmediately(CalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
            SetSectionVisibilityImmediately(FlashcardCalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
            SetSectionVisibilityImmediately(QuizCalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
            SetSectionVisibilityImmediately(MindMapCalendarSectionBodyElement, vm.IsCalendarSectionExpanded);
            SetSectionVisibilityImmediately(GroupsSectionBodyElement, vm.IsGroupsSectionExpanded);
            SetSectionVisibilityImmediately(UngroupedSectionBodyElement, vm.IsUngroupedSectionExpanded);
            SetSidebarWidthImmediately(vm.SidebarWidth);
            SetMassSelectOverlayStateImmediately(vm.IsMassSelectMode);
            EnsureExpandedSectionsNotClipped(vm);
            _lastKnownGroupsFirst = vm.IsGroupsFirst;
        }

        private void SetMassSelectOverlayStateImmediately(bool isVisible)
        {
            var overlay = MassSelectOverlayToolbarElement;
            if (overlay is null)
                return;

            overlay.BeginAnimation(OpacityProperty, null);
            var translate = EnsureMassOverlayTranslateTransform(overlay);
            translate.BeginAnimation(TranslateTransform.YProperty, null);

            if (isVisible)
            {
                overlay.Visibility = Visibility.Visible;
                overlay.Opacity = 1;
                translate.Y = 0;
                return;
            }

            overlay.Visibility = Visibility.Collapsed;
            overlay.Opacity = 0;
            translate.Y = -12;
            SetMassSelectMoreActionsStateImmediately(false);
        }

        private void AnimateMassSelectOverlay(bool isVisible)
        {
            var overlay = MassSelectOverlayToolbarElement;
            if (overlay is null)
                return;

            overlay.BeginAnimation(OpacityProperty, null);
            var translate = EnsureMassOverlayTranslateTransform(overlay);
            translate.BeginAnimation(TranslateTransform.YProperty, null);

            var duration = TimeSpan.FromMilliseconds(190);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (isVisible)
            {
                overlay.Visibility = Visibility.Visible;
                overlay.Opacity = 0;
                translate.Y = -12;

                overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, duration) { EasingFunction = ease });
                return;
            }

            if (overlay.Visibility != Visibility.Visible)
                return;

            var fadeOut = new DoubleAnimation(overlay.Opacity, 0, duration) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                overlay.Visibility = Visibility.Collapsed;
                overlay.Opacity = 0;
                translate.Y = -12;
            };

            overlay.BeginAnimation(OpacityProperty, fadeOut);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, -12, duration) { EasingFunction = ease });
            SetMassSelectMoreActionsStateImmediately(false);
        }

        private static TranslateTransform EnsureMassOverlayTranslateTransform(FrameworkElement overlay)
        {
            if (overlay.RenderTransform is TranslateTransform translate)
                return translate;

            var created = new TranslateTransform(0, 0);
            overlay.RenderTransform = created;
            return created;
        }

        private void SetMassSelectMoreActionsStateImmediately(bool isExpanded)
        {
            _isMassSelectMoreActionsExpanded = isExpanded;
            UpdateMassSelectMoreActionsToggleIcon();

            var panel = MassSelectMoreActionsPanelElement;
            if (panel is null)
                return;

            panel.BeginAnimation(OpacityProperty, null);
            var scale = EnsureMassMoreActionsScaleTransform(panel);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            if (isExpanded)
            {
                panel.Visibility = Visibility.Visible;
                panel.Opacity = 1;
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                return;
            }

            panel.Visibility = Visibility.Collapsed;
            panel.Opacity = 0;
            scale.ScaleX = 0.92;
            scale.ScaleY = 0.92;
        }

        private void ToggleMassSelectMoreActionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel { IsMassSelectMode: true })
                return;

            AnimateMassSelectMoreActions(!_isMassSelectMoreActionsExpanded);
        }

        private void AnimateMassSelectMoreActions(bool expand)
        {
            _isMassSelectMoreActionsExpanded = expand;
            UpdateMassSelectMoreActionsToggleIcon();

            var panel = MassSelectMoreActionsPanelElement;
            if (panel is null)
                return;

            panel.BeginAnimation(OpacityProperty, null);
            var scale = EnsureMassMoreActionsScaleTransform(panel);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            var duration = TimeSpan.FromMilliseconds(170);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (expand)
            {
                panel.Visibility = Visibility.Visible;
                panel.Opacity = 0;
                scale.ScaleX = 0.92;
                scale.ScaleY = 0.92;

                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, duration) { EasingFunction = easing });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, duration) { EasingFunction = easing });
                return;
            }

            if (panel.Visibility != Visibility.Visible)
                return;

            var fadeOut = new DoubleAnimation(panel.Opacity, 0, duration) { EasingFunction = easing };
            fadeOut.Completed += (_, _) =>
            {
                panel.Visibility = Visibility.Collapsed;
                panel.Opacity = 0;
                scale.ScaleX = 0.92;
                scale.ScaleY = 0.92;
            };

            panel.BeginAnimation(OpacityProperty, fadeOut);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, 0.92, duration) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, 0.92, duration) { EasingFunction = easing });
        }

        private void UpdateMassSelectMoreActionsToggleIcon()
        {
            if (MassSelectMoreActionsToggleButtonElement is not Button toggle)
                return;

            toggle.Content = _isMassSelectMoreActionsExpanded ? "<" : ">";
        }

        private static ScaleTransform EnsureMassMoreActionsScaleTransform(FrameworkElement panel)
        {
            if (panel.RenderTransform is ScaleTransform scale)
                return scale;

            var created = new ScaleTransform(0.92, 0.92);
            panel.RenderTransform = created;
            return created;
        }

        private void SetSidebarWidthImmediately(double width)
        {
            if (SidebarColumnElement != null)
                SidebarColumnElement.Width = new GridLength(width, GridUnitType.Pixel);
        }

        private void AnimateSidebarWidth(double targetWidth)
        {
            if (SidebarColumnElement is null)
                return;

            var currentWidth = SidebarColumnElement.ActualWidth;
            if (currentWidth <= 0)
                currentWidth = SidebarColumnElement.Width.Value;

            if (Math.Abs(currentWidth - targetWidth) < 0.5)
            {
                SidebarColumnElement.Width = new GridLength(targetWidth, GridUnitType.Pixel);
                return;
            }

            var animation = new GridLengthAnimation
            {
                From = new GridLength(currentWidth, GridUnitType.Pixel),
                To = new GridLength(targetWidth, GridUnitType.Pixel),
                Duration = TimeSpan.FromMilliseconds(SidebarAnimationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            SidebarColumnElement.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }

        private static void EnsureExpandedSectionsNotClipped(MainViewModel vm)
        {
            if (Application.Current?.MainWindow is not MainWindow window)
                return;

            if (vm.IsRecentSectionExpanded && window.RecentSectionBodyElement is FrameworkElement recent)
                recent.MaxHeight = double.PositiveInfinity;
            if (vm.IsRecentSectionExpanded && window.FlashcardRecentSectionBodyElement is FrameworkElement fcRecent)
                fcRecent.MaxHeight = double.PositiveInfinity;
            if (vm.IsRecentSectionExpanded && window.QuizRecentSectionBodyElement is FrameworkElement qzRecent)
                qzRecent.MaxHeight = double.PositiveInfinity;
            if (vm.IsRecentSectionExpanded && window.MindMapRecentSectionBodyElement is FrameworkElement mmRecent)
                mmRecent.MaxHeight = double.PositiveInfinity;

            if (vm.IsCalendarSectionExpanded && window.CalendarSectionBodyElement is FrameworkElement calendar)
                calendar.MaxHeight = double.PositiveInfinity;
            if (vm.IsCalendarSectionExpanded && window.FlashcardCalendarSectionBodyElement is FrameworkElement fcCalendar)
                fcCalendar.MaxHeight = double.PositiveInfinity;
            if (vm.IsCalendarSectionExpanded && window.QuizCalendarSectionBodyElement is FrameworkElement qzCalendar)
                qzCalendar.MaxHeight = double.PositiveInfinity;
            if (vm.IsCalendarSectionExpanded && window.MindMapCalendarSectionBodyElement is FrameworkElement mmCalendar)
                mmCalendar.MaxHeight = double.PositiveInfinity;

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

        private void DashboardCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || sender is not System.Windows.Controls.Calendar calendar)
                return;

            if (calendar.SelectedDate.HasValue)
                vm.CalendarSelectedDate = calendar.SelectedDate.Value.Date;
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

        private void DashboardScrollViewer_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer || !HasDashboardListingDragData(e))
                return;

            StartDashboardDragAutoScroll(scrollViewer, e);
        }

        private void DashboardScrollViewer_PreviewDragLeave(object sender, DragEventArgs e)
        {
            if (ReferenceEquals(sender, _dashboardDragScrollViewer))
                StopDashboardDragAutoScroll();
        }

        private void DashboardScrollViewer_Drop(object sender, DragEventArgs e)
        {
            if (ReferenceEquals(sender, _dashboardDragScrollViewer))
                StopDashboardDragAutoScroll();
        }

        private void StartDashboardDragAutoScroll(ScrollViewer scrollViewer, DragEventArgs e)
        {
            _dashboardDragScrollViewer = scrollViewer;
            _dashboardDragScrollPosition = e.GetPosition(scrollViewer);

            _dashboardDragScrollTimer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Render,
                (_, _) => TickDashboardDragAutoScroll(),
                Dispatcher);

            if (!_dashboardDragScrollTimer.IsEnabled)
                _dashboardDragScrollTimer.Start();
        }

        private void TickDashboardDragAutoScroll()
        {
            if (_dashboardDragScrollViewer is null)
            {
                StopDashboardDragAutoScroll();
                return;
            }

            AutoScrollDuringDrag(_dashboardDragScrollViewer, _dashboardDragScrollPosition);
        }

        private void StopDashboardDragAutoScroll()
        {
            _dashboardDragScrollTimer?.Stop();
            _dashboardDragScrollViewer = null;
        }

        private static void AutoScrollDuringDrag(ScrollViewer scrollViewer, Point cursorPosition)
        {
            if (cursorPosition.Y < DashboardDragScrollEdgeThreshold)
            {
                var step = GetDashboardDragScrollStep(DashboardDragScrollEdgeThreshold - cursorPosition.Y);
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - step));
            }
            else if (cursorPosition.Y > scrollViewer.ViewportHeight - DashboardDragScrollEdgeThreshold)
            {
                var step = GetDashboardDragScrollStep(cursorPosition.Y - (scrollViewer.ViewportHeight - DashboardDragScrollEdgeThreshold));
                scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + step));
            }
        }

        private static double GetDashboardDragScrollStep(double edgeDistance)
        {
            var factor = Math.Clamp(edgeDistance / DashboardDragScrollEdgeThreshold, 0, 1);
            return DashboardDragScrollMinStep + ((DashboardDragScrollMaxStep - DashboardDragScrollMinStep) * factor);
        }

        private static bool HasDashboardListingDragData(DragEventArgs e)
        {
            return e.Data.GetDataPresent(typeof(FlashcardSetViewModel))
                   || e.Data.GetDataPresent(typeof(MindMapViewModel))
                   || e.Data.GetDataPresent(typeof(QuizViewModel));
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

        private void DashboardView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not true || sender is not FrameworkElement dashboard)
                return;

            if (!IsLoaded)
            {
                dashboard.Opacity = 1;
                var initialScale = EnsureDashboardViewScaleTransform(dashboard);
                initialScale.ScaleX = 1;
                initialScale.ScaleY = 1;
                return;
            }

            AnimateDashboardViewEnter(dashboard);
        }

        private static void AnimateDashboardViewEnter(FrameworkElement dashboard)
        {
            dashboard.BeginAnimation(OpacityProperty, null);
            var scale = EnsureDashboardViewScaleTransform(dashboard);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            dashboard.Opacity = 0;
            const double startScale = 0.995;
            scale.ScaleX = startScale;
            scale.ScaleY = startScale;

            var duration = TimeSpan.FromMilliseconds(DashboardViewAnimationMs);
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

            dashboard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = ease
            });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(startScale, 1, duration)
            {
                EasingFunction = ease
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(startScale, 1, duration)
            {
                EasingFunction = ease
            });
        }

        private static ScaleTransform EnsureDashboardViewScaleTransform(FrameworkElement dashboard)
        {
            if (dashboard.RenderTransform is ScaleTransform scale)
                return scale;

            scale = new ScaleTransform(1, 1);
            dashboard.RenderTransform = scale;
            return scale;
        }

        private void OpenFromFileMenuButton_Click(object sender, RoutedEventArgs e)
        {
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
                    var settings = AppSettingsService.Load();
                    var preferredFontFamily = string.IsNullOrWhiteSpace(settings.PreferredFontFamily)
                        ? "Segoe UI"
                        : settings.PreferredFontFamily;
                    var preferredFontSize = settings.PreferredFontSize > 0
                        ? settings.PreferredFontSize
                        : 14;

                    var doc = new NoteCards.Models.NoteDocument
                    {
                        Title = System.IO.Path.GetFileNameWithoutExtension(path),
                        Content = content,
                        FontFamily = preferredFontFamily,
                        FontSize = preferredFontSize
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

        private void SidebarSearchButton_Click(object sender, RoutedEventArgs e)
        {
            TopSearchButton_Click(sender, e);
        }

        private void SidebarSortButton_Click(object sender, RoutedEventArgs e)
        {
            SortNotesButton_Click(sender, e);
        }

        private void SidebarTagsFilterButton_Click(object sender, RoutedEventArgs e)
        {
            TagsFilterButton_Click(sender, e);
        }

        private void SidebarSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsMenuButton_Click(sender, e);
        }

        private void SidebarAddButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            if (vm.IsFlashcardsView)
            {
                OpenFlashcardSetEditor(vm, null);
                return;
            }

            if (vm.IsQuizzesView)
            {
                CreateQuizButton_Click(sender, e);
                return;
            }

            if (vm.IsMindMapsView)
            {
                CreateMindMapButton_Click(sender, e);
                return;
            }

            vm.AddNoteCommand.Execute(null);
        }

        private void CloseNotesDashboardChrome()
        {
            CollapseTopSearchPanel();

            if (SortNotesPopupElement != null)
                SortNotesPopupElement.IsOpen = false;

            if (DashboardSectionsPopupElement != null)
                DashboardSectionsPopupElement.IsOpen = false;

            TagsFilterPopup.IsOpen = false;
        }

        private void SortNotesButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseTopSearchPanel();
            TagsFilterPopup.IsOpen = false;
            if (DashboardSectionsPopupElement != null)
                DashboardSectionsPopupElement.IsOpen = false;

            if (SortNotesPopupElement != null)
                SortNotesPopupElement.IsOpen = !SortNotesPopupElement.IsOpen;
        }

        private void TagsFilterButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseTopSearchPanel();
            if (SortNotesPopupElement != null)
                SortNotesPopupElement.IsOpen = false;
            if (DashboardSectionsPopupElement != null)
                DashboardSectionsPopupElement.IsOpen = false;
            TagsFilterPopup.IsOpen = !TagsFilterPopup.IsOpen;
        }

        private void DashboardSectionsButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseTopSearchPanel();
            if (SortNotesPopupElement != null)
                SortNotesPopupElement.IsOpen = false;
            TagsFilterPopup.IsOpen = false;
            if (DashboardSectionsPopupElement != null)
                DashboardSectionsPopupElement.IsOpen = !DashboardSectionsPopupElement.IsOpen;
        }

        private void TopSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (TopSearchPanel.Visibility == Visibility.Visible)
            {
                CollapseTopSearchPanel();
                return;
            }

            ExpandTopSearchPanel();
        }

        private void TopSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            CollapseTopSearchPanel();
            e.Handled = true;
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var aboutPanel = this.FindName("AboutPanelControl") as AboutPanel;
            if (aboutPanel != null)
            {
                aboutPanel.DataContext = this.DataContext;
                aboutPanel.ShowAnimated();
            }
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.MainViewModel vm)
            {
                vm.SearchQuery = string.Empty;
            }

            CollapseTopSearchPanel();
        }

        private void ExpandTopSearchPanel()
        {
            TagsFilterPopup.IsOpen = false;
            if (SortNotesPopupElement != null)
                SortNotesPopupElement.IsOpen = false;
            if (DashboardSectionsPopupElement != null)
                DashboardSectionsPopupElement.IsOpen = false;

            TopSearchPanel.Visibility = Visibility.Visible;
            TopSearchPanel.IsHitTestVisible = true;
            TopSearchPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
            TopSearchPanel.BeginAnimation(OpacityProperty, null);

            TopSearchPanel.Width = 0;
            TopSearchPanel.Opacity = 0;

            var duration = TimeSpan.FromMilliseconds(TopSearchAnimationMs);
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            TopSearchPanel.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(0, TopSearchExpandedWidth, duration)
            {
                EasingFunction = easeOut
            });
            TopSearchPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = easeOut
            });

            Dispatcher.BeginInvoke(() =>
            {
                TopSearchTextBox.Focus();
                TopSearchTextBox.SelectAll();
            }, DispatcherPriority.Input);
        }

        private void CollapseTopSearchPanel()
        {
            if (TopSearchPanel.Visibility != Visibility.Visible)
                return;

            // Read current visual state first, then animate to collapsed state.
            // Clearing animations before this can snap values and make collapse look instant.
            var startWidth = TopSearchPanel.ActualWidth > 0
                ? TopSearchPanel.ActualWidth
                : Math.Max(TopSearchPanel.Width, 1);
            var startOpacity = TopSearchPanel.Opacity;

            if (startOpacity <= 0)
                startOpacity = 1;

            var duration = TimeSpan.FromMilliseconds(TopSearchAnimationMs);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var widthAnimation = new DoubleAnimation(startWidth, 0, duration)
            {
                EasingFunction = easeIn
            };
            widthAnimation.Completed += (_, _) =>
            {
                TopSearchPanel.Visibility = Visibility.Collapsed;
                TopSearchPanel.IsHitTestVisible = false;
                TopSearchPanel.Width = 0;
                TopSearchPanel.Opacity = 0;
            };

            TopSearchPanel.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
            TopSearchPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(startOpacity, 0, duration)
            {
                EasingFunction = easeIn
            });
        }

        // Open editor for a specific note card
        public void OpenNoteEditor(NoteCardViewModel noteViewModel)
        {
            if (_noteEditorTabsWindow == null || !_noteEditorTabsWindow.IsLoaded)
            {
                _noteEditorTabsWindow = new NoteEditorTabsWindow
                {
                    Owner = this
                };

                _noteEditorTabsWindow.Closed += (_, _) =>
                {
                    _noteEditorTabsWindow = null;
                };

                _noteEditorTabsWindow.Show();
            }

            _noteEditorTabsWindow.OpenOrFocusNote(noteViewModel, DataContext);
            _noteEditorTabsWindow.Activate();
        }

        public void OpenNoteSchedule(NoteCardViewModel noteViewModel)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var schedulePanel = FindName("NoteSchedulePanelControl") as NoteSchedulePanel;
            schedulePanel?.ShowAnimated(vm, noteViewModel);
        }

        private void CreateFlashcardsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                OpenFlashcardSetEditor(vm, null);
        }

        private void CreateQuizButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                OpenQuizEditor(vm, null);
        }

        private void CreateMindMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            // Create a blank root node with a default title
            var rootNode = new MindMapNode { Text = "Central Topic" };

            var editor = new MindMapPreviewWindow(
                rootNode,
                modelDisplayName: null,
                title: null,
                tags: null,
                layoutMode: null,
                useManualPositions: false)
            {
                Owner = this
            };

            MindMapDocument? savedDocument = null;
            var autoSaveTimer = StartEditorAutoSave(
                editor.HasPendingAutoSaveChanges,
                () =>
                {
                    var updatedDocument = editor.ToDocument(savedDocument);
                    savedDocument = vm.AddOrUpdateMindMap(updatedDocument).Document;
                    editor.MarkCurrentStateAutoSaved();
                });

            try
            {
                if (editor.ShowDialog() == true)
                {
                    var newDocument = editor.ToDocument(savedDocument);
                    vm.AddOrUpdateMindMap(newDocument);
                    vm.SaveMindMaps();
                }
            }
            finally
            {
                StopEditorAutoSave(autoSaveTimer);
            }
        }

        private void OpenFlashcardSetEditor(MainViewModel vm, FlashcardSetViewModel? set)
        {
            var document = set?.Document;
            var editor = new FlashcardsPreviewWindow(
                document?.Cards ?? Enumerable.Empty<FlashcardItem>(),
                document?.AiModelDisplayName,
                document?.Title,
                document?.Tags,
                document?.SetNames,
                document?.StudySession,
                mainWindow: this)
            {
                Owner = this
            };

            FlashcardSetDocument? savedDocument = document;
            var autoSaveTimer = StartEditorAutoSave(
                editor.HasPendingAutoSaveChanges,
                () =>
                {
                    var updatedDocument = editor.ToDocument(savedDocument);
                    savedDocument = vm.AddOrUpdateFlashcardSet(updatedDocument).Document;
                    editor.MarkCurrentStateAutoSaved();
                });

            try
            {
                if (editor.ShowDialog() == true)
                    vm.AddOrUpdateFlashcardSet(editor.ToDocument(savedDocument));
            }
            finally
            {
                StopEditorAutoSave(autoSaveTimer);
            }
        }

        private void OpenFlashcardSetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: FlashcardSetViewModel set })
                OpenFlashcardSetEditor(vm, set);
        }

        private void OpenQuizEditor(MainViewModel vm, QuizViewModel? quiz)
        {
            var document = quiz?.Document ?? new QuizDocument
            {
                Title = LocalizationService.GetString("QuizUntitled"),
                Questions = new List<QuizQuestion>
                {
                    new()
                    {
                        Type = QuizQuestionType.SingleChoice,
                        Question = LocalizationService.GetString("NewQuizQuestion"),
                        Options = new List<QuizOption>
                        {
                            new() { Text = LocalizationService.GetString("NewQuizCorrectAnswer"), IsCorrect = true },
                            new() { Text = LocalizationService.GetString("NewQuizWrongAnswer"), IsCorrect = false },
                            new() { Text = LocalizationService.GetString("NewQuizWrongAnswer"), IsCorrect = false }
                        }
                    }
                }
            };

            var editor = new QuizPreviewWindow(
                document,
                vm.Notes.Select(note => new QuizPreviewWindow.QuizNoteLinkOption(note.Document.Id, note.Document.Title)),
                document.AiModelDisplayName,
                document.Title,
                noteId => OpenNoteById(vm, noteId))
            {
                Owner = this
            };

            QuizDocument? savedDocument = quiz?.Document;
            var autoSaveTimer = StartEditorAutoSave(
                editor.HasPendingAutoSaveChanges,
                () =>
                {
                    var updatedDocument = editor.ToDocument(savedDocument);
                    savedDocument = vm.AddOrUpdateQuiz(updatedDocument).Document;
                    editor.MarkCurrentStateAutoSaved();
                });

            try
            {
                if (editor.ShowDialog() == true)
                    vm.AddOrUpdateQuiz(editor.ToDocument(savedDocument));
            }
            finally
            {
                StopEditorAutoSave(autoSaveTimer);
            }
        }

        private void OpenQuizLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var window = new QuizLibraryWindow(vm)
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void OpenAttemptHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var savedAttempts = NoteCards.Models.QuizDocument.SavedAttempts;
            var window = new NoteCards.Views.QuizAttemptHistoryWindow()
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void OpenNoteById(MainViewModel vm, Guid noteId)
        {
            var note = vm.FindNoteById(noteId);
            if (note is null)
                return;

            OpenNoteEditor(note);
        }

        private QuizViewModel? GetQuizFromMenuSender(object sender)
        {
            if (sender is not MenuItem menuItem)
                return null;

            var contextMenu = menuItem.Parent as ContextMenu;
            var target = contextMenu?.PlacementTarget as FrameworkElement;
            return target?.DataContext as QuizViewModel;
        }

        private void DeleteQuizMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var quiz = GetQuizFromMenuSender(sender);
            if (quiz is null)
                return;

            if (DataContext is not MainViewModel vm)
                return;

            var dialog = new DeleteConfirmationDialog(
                LocalizationService.GetString("DeleteQuiz"),
                string.Format(LocalizationService.GetString("DeleteQuizConfirmationFormat"), quiz.Title))
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
                vm.DeleteQuiz(quiz);
        }

        private void OpenQuizMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm
                && sender is MenuItem menuItem
                && menuItem.DataContext is QuizViewModel quizVm)
            {
                OpenQuizEditor(vm, quizVm);
            }
        }

        private void DuplicateQuizMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is QuizViewModel quizVm)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.DuplicateQuiz(quizVm);
                }
            }
        }

        private void ToggleQuizPinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: QuizViewModel quizVm })
                vm.ToggleQuizPin(quizVm);
        }

        private void RemoveQuizFromGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: QuizViewModel quizVm })
                vm.RemoveQuizFromGroup(quizVm);
        }

        private void QuizInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var quiz = GetQuizFromMenuSender(sender)
                ?? (sender as MenuItem)?.DataContext as QuizViewModel;

            if (quiz != null)
                ShowQuizInfo(quiz);
        }

        private FlashcardSetViewModel? GetFlashcardSetFromMenuSender(object sender)
        {
            if (sender is not MenuItem menuItem)
                return null;

            var contextMenu = menuItem.Parent as ContextMenu;
            var target = contextMenu?.PlacementTarget as FrameworkElement;
            return target?.DataContext as FlashcardSetViewModel;
        }

        private void DeleteFlashcardSetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var set = GetFlashcardSetFromMenuSender(sender);
            if (set is null)
                return;

            if (DataContext is not MainViewModel vm)
                return;

            var dialog = new DeleteConfirmationDialog(
                LocalizationService.GetString("DeleteFlashcardSet"),
                string.Format(LocalizationService.GetString("DeleteFlashcardSetConfirmationFormat"), set.Title))
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
                vm.DeleteFlashcardSet(set);
        }

        private void DuplicateFlashcardSetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: FlashcardSetViewModel set })
                vm.DuplicateFlashcardSet(set);
        }

        private void ToggleFlashcardSetPinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: FlashcardSetViewModel set })
                vm.ToggleFlashcardSetPin(set);
        }

        private void RemoveFlashcardSetFromGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: FlashcardSetViewModel set })
                vm.RemoveFlashcardSetFromGroup(set);
        }

        private void FlashcardSetInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var set = GetFlashcardSetFromMenuSender(sender)
                ?? (sender as MenuItem)?.DataContext as FlashcardSetViewModel;

            if (set != null)
                ShowFlashcardSetInfo(set);
        }

        private void OpenMindMapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is MindMapViewModel mindMapVm)
            {
                OpenMindMapEditor(mindMapVm);
            }
        }

        private async void DuplicateMindMapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.DataContext is not MindMapViewModel mindMapVm)
                return;

            // Duplicate the mind map
            if (DataContext is MainViewModel vm)
            {
                var newMindMap = vm.DuplicateMindMap(mindMapVm);
                if (newMindMap != null)
                {
                    // Optionally open the editor for the duplicated mind map
                    // OpenMindMapEditor(newMindMap);
                }
            }
        }

        private void ToggleMindMapPinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: MindMapViewModel mindMapVm })
                vm.ToggleMindMapPin(mindMapVm);
        }

        private async void DeleteMindMapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.DataContext is not MindMapViewModel mindMapVm)
                return;

            var dialog = new DeleteConfirmationDialog(
                LocalizationService.GetString("DeleteMindMap"),
                string.Format(
                    LocalizationService.GetString("DeleteMindMapConfirmationFormat"),
                    mindMapVm.Title))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            // Delete the mind map
            if (DataContext is MainViewModel vm)
            {
                vm.DeleteMindMap(mindMapVm);
            }
        }

        private void RemoveMindMapFromGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem { DataContext: MindMapViewModel mindMapVm })
                vm.RemoveMindMapFromGroup(mindMapVm);
        }

        private void MindMapInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { DataContext: MindMapViewModel mindMapVm })
                ShowMindMapInfo(mindMapVm);
        }

        private void OpenMindMapEditor(MindMapViewModel mindMapVm)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var editor = new MindMapPreviewWindow(
                mindMapVm.Document.Root,
                vm.Notes.Select(note => new MindMapPreviewWindow.MindMapNoteLinkOption(note.Document.Id, note.Document.Title)),
                mindMapVm.Document.AiModelDisplayName,
                mindMapVm.Document.Title,
                mindMapVm.Document.Tags,
                mindMapVm.Document.LayoutMode,
                mindMapVm.Document.UseManualPositions,
                mindMapVm.Document.SourceNoteId,
                noteId => OpenNoteById(vm, noteId))
            {
                Owner = this
            };

            MindMapDocument? savedDocument = mindMapVm.Document;
            var autoSaveTimer = StartEditorAutoSave(
                editor.HasPendingAutoSaveChanges,
                () =>
                {
                    var updatedDocument = editor.ToDocument(savedDocument);
                    savedDocument = vm.AddOrUpdateMindMap(updatedDocument).Document;
                    editor.MarkCurrentStateAutoSaved();
                });

            try
            {
                if (editor.ShowDialog() == true)
                    vm.AddOrUpdateMindMap(editor.ToDocument(savedDocument));
            }
            finally
            {
                StopEditorAutoSave(autoSaveTimer);
            }
        }

        private DispatcherTimer? StartEditorAutoSave(Func<bool> hasChanges, Action saveAction)
        {
            var settings = AppSettingsService.Load();
            if (!settings.EnableAutoSave)
                return null;

            var intervalSeconds = Math.Clamp(settings.AutoSaveIntervalSeconds, 5, 86400);
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(intervalSeconds)
            };

            timer.Tick += (_, _) =>
            {
                if (!AppSettingsService.Load().EnableAutoSave)
                {
                    timer.Stop();
                    return;
                }

                if (!hasChanges())
                    return;

                try
                {
                    saveAction();
                }
                catch
                {
                    // Auto-save should never interrupt editing.
                }
            };
            timer.Start();
            return timer;
        }

        private static void StopEditorAutoSave(DispatcherTimer? timer)
        {
            timer?.Stop();
        }

        private static bool IsWithinCalendarScheduleGearButton(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Button button && string.Equals(button.Name, "CalendarScheduleGearButton", StringComparison.Ordinal))
                    return true;

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        // Settings menu button click handler
        private void SettingsMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsPanel = this.FindName("SettingsPanelControl") as SettingsPanel;
            if (settingsPanel != null)
            {
                settingsPanel.DataContext = this.DataContext;
                settingsPanel.ShowAnimated();
            }
        }
        private void RecentNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NoteCardViewModel noteVm)
                OpenNoteEditor(noteVm);
        }

        private void RecentFlashcardSetButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is Button { Tag: FlashcardSetViewModel set })
                OpenFlashcardSetEditor(vm, set);
        }

        private void RecentMindMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: MindMapViewModel mindMap })
                OpenMindMapEditor(mindMap);
        }

        private void RecentQuizButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is Button { Tag: QuizViewModel quiz })
                OpenQuizEditor(vm, quiz);
        }

        private void CalendarScheduledItemCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && IsWithinCalendarScheduleGearButton(source))
                return;

            if (sender is not Border { Tag: CalendarScheduledItemViewModel item })
                return;

            if (DataContext is not MainViewModel vm)
                return;

            switch (item.ItemType)
            {
                case ScheduledItemType.Note when item.Note != null:
                    OpenNoteEditor(item.Note);
                    break;
                case ScheduledItemType.Flashcard when item.FlashcardSet != null:
                    OpenFlashcardSetEditor(vm, item.FlashcardSet);
                    break;
                case ScheduledItemType.MindMap when item.MindMap != null:
                    OpenMindMapEditor(item.MindMap);
                    break;
                case ScheduledItemType.Quiz when item.Quiz != null:
                    OpenQuizEditor(vm, item.Quiz);
                    break;
            }
        }

        private void CalendarScheduleGearButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: CalendarScheduledItemViewModel item })
            {
                switch (item.ItemType)
                {
                    case ScheduledItemType.Note when item.Note != null:
                        OpenNoteSchedule(item.Note);
                        break;
                    case ScheduledItemType.Flashcard when item.FlashcardSet != null:
                        OpenFlashcardSetSchedule(item.FlashcardSet);
                        break;
                    case ScheduledItemType.MindMap when item.MindMap != null:
                        OpenMindMapSchedule(item.MindMap);
                        break;
                    case ScheduledItemType.Quiz when item.Quiz != null:
                        OpenQuizSchedule(item.Quiz);
                        break;
                }
            }

            e.Handled = true;
        }

        private void OpenFlashcardSetSchedule(FlashcardSetViewModel flashcardSet)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var schedulePanel = FindName("NoteSchedulePanelControl") as NoteSchedulePanel;
            var subtitle = $"{LocalizationService.GetString("FlashcardsDashboard")} · {flashcardSet.Document.Title}";
            schedulePanel?.ShowAnimated(vm, flashcardSet.Document.Schedules ?? Enumerable.Empty<NoteScheduleEntry>(),
                entries => vm.SetFlashcardSetSchedules(flashcardSet, entries),
                LocalizationService.GetString("CalendarAssignMenu"), subtitle);
        }

        private void OpenMindMapSchedule(MindMapViewModel mindMap)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var schedulePanel = FindName("NoteSchedulePanelControl") as NoteSchedulePanel;
            var subtitle = $"{LocalizationService.GetString("MindMapsDashboard")} · {mindMap.Document.Title}";
            schedulePanel?.ShowAnimated(vm, mindMap.Document.Schedules ?? Enumerable.Empty<NoteScheduleEntry>(),
                entries => vm.SetMindMapSchedules(mindMap, entries),
                LocalizationService.GetString("CalendarAssignMenu"), subtitle);
        }

        private void OpenQuizSchedule(QuizViewModel quiz)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var schedulePanel = FindName("NoteSchedulePanelControl") as NoteSchedulePanel;
            var subtitle = $"{LocalizationService.GetString("QuizzesDashboard")} · {quiz.Document.Title}";
            schedulePanel?.ShowAnimated(vm, quiz.Document.Schedules ?? Enumerable.Empty<NoteScheduleEntry>(),
                entries => vm.SetQuizSchedules(quiz, entries),
                LocalizationService.GetString("CalendarAssignMenu"), subtitle);
        }

        private void ScheduleFlashcardSetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var set = GetFlashcardSetFromMenuSender(sender);
            if (set != null)
                OpenFlashcardSetSchedule(set);
        }

        private void ScheduleMindMapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is MindMapViewModel mindMapVm)
                OpenMindMapSchedule(mindMapVm);
        }

        private void ScheduleQuizMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is QuizViewModel quizVm)
                OpenQuizSchedule(quizVm);
        }

        public void ShowNoteInfo(NoteCardViewModel note)
        {
            var schedules = GetNoteSchedules(note.Document);
            var content = note.Content ?? string.Empty;
            var rows = new List<(string Label, string Value)>
            {
                (InfoText("InfoLabelType"), InfoText("InfoTypeNote")),
                (InfoText("InfoLabelTitle"), string.IsNullOrWhiteSpace(note.Title) ? LocalizationService.GetString("NewNoteTitle") : note.Title),
                (InfoText("InfoLabelCreated"), FormatInfoDate(note.Document.CreatedAt)),
                (InfoText("InfoLabelModified"), FormatInfoDate(note.Document.LastModified)),
                (InfoText("InfoLabelGroup"), GetNoteInfoGroupName(note.Document.GroupId)),
                (InfoText("InfoLabelPinned"), FormatInfoBool(note.Document.IsPinned)),
                (InfoText("InfoLabelScheduled"), FormatScheduleInfo(schedules)),
                (InfoText("InfoLabelTags"), FormatInfoTags(note.Document.Tags)),
                (InfoText("InfoLabelWords"), CountWords(content).ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelCharacters"), content.Length.ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelImages"), (note.Document.Images?.Count ?? 0).ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelEditHistory"), (note.Document.EditHistory?.Count ?? 0).ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelFont"), $"{note.Document.FontFamily}, {note.Document.FontSize.ToString("0.#", CultureInfo.CurrentCulture)}"),
                (InfoText("InfoLabelId"), note.Document.Id.ToString())
            };

            ShowItemInfoDialog(note.Title, rows.Take(8), rows.Skip(8));
        }

        private void ShowFlashcardSetInfo(FlashcardSetViewModel set)
        {
            var cards = set.Document.Cards ?? new List<FlashcardItem>();
            var rows = new List<(string Label, string Value)>
            {
                (InfoText("InfoLabelType"), InfoText("InfoTypeFlashcards")),
                (InfoText("InfoLabelTitle"), set.Title),
                (InfoText("InfoLabelCreated"), FormatInfoDate(set.Document.CreatedAt)),
                (InfoText("InfoLabelModified"), FormatInfoDate(set.Document.LastModified)),
                (InfoText("InfoLabelGroup"), GetFlashcardSetInfoGroupName(set.Document.GroupId)),
                (InfoText("InfoLabelPinned"), FormatInfoBool(set.Document.IsPinned)),
                (InfoText("InfoLabelScheduled"), FormatScheduleInfo(set.Document.Schedules)),
                (InfoText("InfoLabelTags"), FormatInfoTags(set.Document.Tags)),
                (InfoText("InfoLabelCards"), set.CardCount.ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelSets"), set.SetCount.ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelKnownCards"), cards.Count(card => card.IsKnown).ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelUnknownCards"), cards.Count(card => card.IsUnknown).ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelCategories"), cards.Select(card => card.Category?.Trim()).Where(category => !string.IsNullOrWhiteSpace(category)).Distinct(StringComparer.CurrentCultureIgnoreCase).Count().ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelGeneratedWith"), FormatOptional(set.Document.AiModelDisplayName)),
                (InfoText("InfoLabelId"), set.Document.Id.ToString())
            };

            ShowItemInfoDialog(set.Title, rows.Take(8), rows.Skip(8));
        }

        private void ShowMindMapInfo(MindMapViewModel mindMap)
        {
            var rows = new List<(string Label, string Value)>
            {
                (InfoText("InfoLabelType"), InfoText("InfoTypeMindMap")),
                (InfoText("InfoLabelTitle"), mindMap.Title),
                (InfoText("InfoLabelCreated"), FormatInfoDate(mindMap.Document.CreatedAt)),
                (InfoText("InfoLabelModified"), FormatInfoDate(mindMap.Document.LastModified)),
                (InfoText("InfoLabelGroup"), GetMindMapInfoGroupName(mindMap.Document.GroupId)),
                (InfoText("InfoLabelPinned"), FormatInfoBool(mindMap.Document.IsPinned)),
                (InfoText("InfoLabelScheduled"), FormatScheduleInfo(mindMap.Document.Schedules)),
                (InfoText("InfoLabelTags"), FormatInfoTags(mindMap.Document.Tags)),
                (InfoText("InfoLabelNodes"), mindMap.NodeCount.ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelBranches"), mindMap.BranchCount.ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelLayout"), FormatMindMapLayout(mindMap.Document.LayoutMode)),
                (InfoText("InfoLabelManualPositioning"), FormatInfoBool(mindMap.Document.UseManualPositions)),
                (InfoText("InfoLabelSourceNote"), GetSourceNoteInfoTitle(mindMap.Document.SourceNoteId)),
                (InfoText("InfoLabelGeneratedWith"), FormatOptional(mindMap.Document.AiModelDisplayName)),
                (InfoText("InfoLabelId"), mindMap.Document.Id.ToString())
            };

            ShowItemInfoDialog(mindMap.Title, rows.Take(8), rows.Skip(8));
        }

        private void ShowQuizInfo(QuizViewModel quiz)
        {
            var attempts = quiz.Document.Attempts ?? new List<QuizAttempt>();
            var rows = new List<(string Label, string Value)>
            {
                (InfoText("InfoLabelType"), InfoText("InfoTypeQuiz")),
                (InfoText("InfoLabelTitle"), quiz.Title),
                (InfoText("InfoLabelCreated"), FormatInfoDate(quiz.Document.CreatedAt)),
                (InfoText("InfoLabelModified"), FormatInfoDate(quiz.Document.LastModified)),
                (InfoText("InfoLabelGroup"), GetQuizInfoGroupName(quiz.Document.GroupId)),
                (InfoText("InfoLabelPinned"), FormatInfoBool(quiz.Document.IsPinned)),
                (InfoText("InfoLabelScheduled"), FormatScheduleInfo(quiz.Document.Schedules)),
                (InfoText("InfoLabelTags"), FormatInfoTags(quiz.Document.Tags)),
                (InfoText("InfoLabelQuestions"), quiz.QuestionCount.ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelPassingScore"), $"{quiz.Document.PassingScorePercent.ToString(CultureInfo.CurrentCulture)}%"),
                (InfoText("InfoLabelTimeLimit"), FormatTimeLimit(quiz.Document.TimeLimitSeconds)),
                (InfoText("InfoLabelAttempts"), attempts.Count.ToString(CultureInfo.CurrentCulture)),
                (InfoText("InfoLabelBestScore"), FormatBestQuizScore(attempts)),
                (InfoText("InfoLabelSourceNote"), GetSourceNoteInfoTitle(quiz.Document.SourceNoteId)),
                (InfoText("InfoLabelGeneratedWith"), FormatOptional(quiz.Document.AiModelDisplayName)),
                (InfoText("InfoLabelId"), quiz.Document.Id.ToString())
            };

            ShowItemInfoDialog(quiz.Title, rows.Take(8), rows.Skip(8));
        }

        private void ShowItemInfoDialog(
            string itemTitle,
            IEnumerable<(string Label, string Value)> primaryRows,
            IEnumerable<(string Label, string Value)> advancedRows)
        {
            var title = string.IsNullOrWhiteSpace(itemTitle)
                ? LocalizationService.GetString("Info")
                : $"{itemTitle.Trim()} - {LocalizationService.GetString("Info")}";

            var dialog = new ItemInfoDialog(title, primaryRows, advancedRows)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private static string FormatInfoDate(DateTime value)
        {
            if (value == default)
                return InfoText("InfoUnknown");

            var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        }

        private static string FormatInfoBool(bool value) => value ? InfoText("InfoYes") : InfoText("InfoNo");

        private static string FormatOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? InfoText("InfoNone") : value.Trim();
        }

        private static string FormatInfoTags(IEnumerable<string>? tags)
        {
            var cleaned = tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList() ?? new List<string>();

            return cleaned.Count == 0 ? InfoText("InfoNone") : string.Join(", ", cleaned);
        }

        private static string FormatScheduleInfo(IEnumerable<NoteScheduleEntry>? schedules)
        {
            var ordered = schedules?
                .OrderBy(schedule => schedule.ScheduledAt)
                .ToList() ?? new List<NoteScheduleEntry>();

            if (ordered.Count == 0)
                return InfoText("InfoNo");

            var now = DateTime.Now;
            var upcoming = ordered.FirstOrDefault(schedule => schedule.ScheduledAt >= now);
            var selected = upcoming ?? ordered[^1];
            var formatKey = upcoming is null ? "InfoScheduleLatestFormat" : "InfoScheduleNextFormat";
            return string.Format(
                CultureInfo.CurrentCulture,
                InfoText(formatKey),
                ordered.Count.ToString(CultureInfo.CurrentCulture),
                FormatInfoDate(selected.ScheduledAt));
        }

        private static IReadOnlyList<NoteScheduleEntry> GetNoteSchedules(NoteDocument document)
        {
            var schedules = document.Schedules?.ToList() ?? new List<NoteScheduleEntry>();
            if (document.ScheduledAt.HasValue
                && !schedules.Any(schedule => schedule.ScheduledAt == document.ScheduledAt.Value))
            {
                schedules.Add(new NoteScheduleEntry
                {
                    ScheduledAt = document.ScheduledAt.Value,
                    Note = document.ScheduleNote
                });
            }

            return schedules.OrderBy(schedule => schedule.ScheduledAt).ToList();
        }

        private string GetNoteInfoGroupName(Guid? groupId)
        {
            if (!groupId.HasValue)
                return InfoText("InfoUngrouped");

            if (DataContext is MainViewModel vm)
            {
                var group = vm.NoteGroups.FirstOrDefault(candidate => candidate.GroupId == groupId.Value);
                if (group != null && !string.IsNullOrWhiteSpace(group.Name))
                    return group.Name;
            }

            return FormatGroupFallback(groupId.Value);
        }

        private string GetFlashcardSetInfoGroupName(Guid? groupId)
        {
            if (!groupId.HasValue)
                return InfoText("InfoUngrouped");

            if (DataContext is MainViewModel vm)
            {
                var group = vm.FlashcardSetGroups.FirstOrDefault(candidate => candidate.GroupId == groupId.Value);
                if (group != null && !string.IsNullOrWhiteSpace(group.Name))
                    return group.Name;
            }

            return FormatGroupFallback(groupId.Value);
        }

        private string GetMindMapInfoGroupName(Guid? groupId)
        {
            if (!groupId.HasValue)
                return InfoText("InfoUngrouped");

            if (DataContext is MainViewModel vm)
            {
                var group = vm.MindMapGroups.FirstOrDefault(candidate => candidate.GroupId == groupId.Value);
                if (group != null && !string.IsNullOrWhiteSpace(group.Name))
                    return group.Name;
            }

            return FormatGroupFallback(groupId.Value);
        }

        private string GetQuizInfoGroupName(Guid? groupId)
        {
            if (!groupId.HasValue)
                return InfoText("InfoUngrouped");

            if (DataContext is MainViewModel vm)
            {
                var group = vm.QuizGroups.FirstOrDefault(candidate => candidate.GroupId == groupId.Value);
                if (group != null && !string.IsNullOrWhiteSpace(group.Name))
                    return group.Name;
            }

            return FormatGroupFallback(groupId.Value);
        }

        private string GetSourceNoteInfoTitle(Guid? sourceNoteId)
        {
            if (!sourceNoteId.HasValue)
                return InfoText("InfoNone");

            if (DataContext is MainViewModel vm)
            {
                var note = vm.FindNoteById(sourceNoteId.Value);
                if (note != null && !string.IsNullOrWhiteSpace(note.Title))
                    return note.Title;
            }

            return sourceNoteId.Value.ToString();
        }

        private static string FormatGroupFallback(Guid groupId)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                InfoText("InfoGroupFallbackFormat"),
                groupId.ToString()[..4].ToUpperInvariant());
        }

        private static string FormatMindMapLayout(string? layoutMode)
        {
            return layoutMode switch
            {
                "BalancedTree" => LocalizationService.GetString("MindMapLayoutBalancedTree"),
                "RightTree" => LocalizationService.GetString("MindMapLayoutRightTree"),
                "LeftTree" => LocalizationService.GetString("MindMapLayoutLeftTree"),
                "TopDown" => LocalizationService.GetString("MindMapLayoutTopDown"),
                "Radial" => LocalizationService.GetString("MindMapLayoutRadial"),
                _ => FormatOptional(layoutMode)
            };
        }

        private static string FormatTimeLimit(int? seconds)
        {
            if (!seconds.HasValue || seconds.Value <= 0)
                return InfoText("InfoNoLimit");

            var duration = TimeSpan.FromSeconds(seconds.Value);
            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture)
                : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);
        }

        private static string FormatBestQuizScore(IReadOnlyCollection<QuizAttempt> attempts)
        {
            if (attempts.Count == 0)
                return InfoText("InfoNone");

            var best = attempts.Max(attempt => attempt.Percentage);
            return $"{best.ToString("0.#", CultureInfo.CurrentCulture)}%";
        }

        private static int CountWords(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string InfoText(string key) => LocalizationService.GetString(key);

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

        private void ToggleCalendarSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsCalendarSectionExpanded = !vm.IsCalendarSectionExpanded;
        }

        private void ToggleUngroupedSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsUngroupedSectionExpanded = !vm.IsUngroupedSectionExpanded;
        }

        private void ExitMassSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.ExitMassSelect();
        }

        private void AddTagsToSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || !vm.IsMassSelectMode)
                return;

            var dialog = new SimpleInputDialog(
                LocalizationService.GetString("MassSelectAddTagsTitle"),
                LocalizationService.GetString("MassSelectAddTagsPrompt"))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            vm.AddTagsToSelected(dialog.InputText);
        }

        private void DeleteSelectedMassButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || !vm.IsMassSelectMode || vm.SelectedNotesCount <= 0)
                return;

            var message = string.Format(
                LocalizationService.GetString("DeleteSelectedNotesConfirmation"),
                vm.SelectedNotesCount);

            var dialog = new DeleteConfirmationDialog(
                title: LocalizationService.GetString("DeleteNotesTitle"),
                message: message)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            if (vm.DeleteSelectedNotesCommand.CanExecute(null))
                vm.DeleteSelectedNotesCommand.Execute(null);
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

        private void MoveCalendarUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsCalendarFirst = true;
        }

        private void MoveCalendarDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsCalendarFirst = false;
        }

        private void MoveDashboardContentUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsCalendarFirst = false;
        }

        private void MoveDashboardContentDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsCalendarFirst = true;
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

        private void ListingContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button menuButton)
                return;

            var cardElement = FindAncestorDashboardListingWithContextMenu(menuButton);
            if (cardElement?.ContextMenu is null)
                return;

            cardElement.ContextMenu.PlacementTarget = menuButton;
            cardElement.ContextMenu.Placement = PlacementMode.Bottom;
            cardElement.ContextMenu.IsOpen = true;
            e.Handled = true;
        }

        private void DashboardListing_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dashboardListingDragStart = sender is IInputElement inputElement
                ? e.GetPosition(inputElement)
                : e.GetPosition(this);
        }

        private void DashboardListing_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (IsWithinListingContextMenuButton(e.OriginalSource as DependencyObject))
                return;

            var listing = ResolveDashboardListingFromElement(element);
            if (listing is null)
                return;

            var current = e.GetPosition(element);
            var delta = current - _dashboardListingDragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _suppressDashboardListingOpen = true;
            try
            {
                DragDrop.DoDragDrop(element, CreateDashboardListingDataObject(listing), DragDropEffects.Move);
                e.Handled = true;
            }
            finally
            {
                StopDashboardDragAutoScroll();
            }
        }

        private void DashboardListing_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_suppressDashboardListingOpen)
            {
                _suppressDashboardListingOpen = false;
                e.Handled = true;
                return;
            }

            if (IsWithinListingContextMenuButton(e.OriginalSource as DependencyObject))
                return;

            var listing = ResolveDashboardListingFromElement(sender as FrameworkElement);
            switch (listing)
            {
                case FlashcardSetViewModel flashcardSet when DataContext is MainViewModel vm:
                    OpenFlashcardSetEditor(vm, flashcardSet);
                    e.Handled = true;
                    break;
                case MindMapViewModel mindMap:
                    OpenMindMapEditor(mindMap);
                    e.Handled = true;
                    break;
                case QuizViewModel quiz when DataContext is MainViewModel vm:
                    OpenQuizEditor(vm, quiz);
                    e.Handled = true;
                    break;
            }
        }

        private void DashboardListing_PreviewDragEnter(object sender, DragEventArgs e)
        {
            var canDrop = CanDropDashboardListing(sender, e);
            SetDashboardListingDropVisual(sender as DependencyObject, canDrop);
            e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void DashboardListing_PreviewDragOver(object sender, DragEventArgs e)
        {
            var canDrop = CanDropDashboardListing(sender, e);
            SetDashboardListingDropVisual(sender as DependencyObject, canDrop);
            e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void DashboardListing_PreviewDragLeave(object sender, DragEventArgs e)
        {
            SetDashboardListingDropVisual(sender as DependencyObject, false);
            e.Handled = true;
        }

        private void DashboardListing_PreviewDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement targetElement)
                    return;

                TryHandleDashboardListingDrop(targetElement.Tag, e, targetElement);
            }
            finally
            {
                SetDashboardListingDropVisual(sender as DependencyObject, false);
            }
        }

        private void DashboardGroupBorder_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (FindDashboardListingElementFromDragEvent(e) is FrameworkElement listingElement
                && CanDropDashboardListing(listingElement, e))
            {
                e.Effects = DragDropEffects.Move;
                return;
            }

            e.Effects = CanDropDashboardListingOnGroup(sender, e) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void DashboardGroupBorder_PreviewDrop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel vm || sender is not FrameworkElement targetElement)
                return;

            if (FindDashboardListingElementFromDragEvent(e) is FrameworkElement listingElement
                && CanDropDashboardListing(listingElement, e))
                return;

            var changed = targetElement.Tag switch
            {
                FlashcardSetGroupViewModel target when e.Data.GetData(typeof(FlashcardSetViewModel)) is FlashcardSetViewModel dragged
                    => vm.TryMoveFlashcardSetToGroup(dragged, target),
                MindMapGroupViewModel target when e.Data.GetData(typeof(MindMapViewModel)) is MindMapViewModel dragged
                    => vm.TryMoveMindMapToGroup(dragged, target),
                QuizGroupViewModel target when e.Data.GetData(typeof(QuizViewModel)) is QuizViewModel dragged
                    => vm.TryMoveQuizToGroup(dragged, target),
                _ => false
            };

            if (changed)
                e.Handled = true;
        }

        private void DashboardUngroupedDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (FindDashboardListingElementFromDragEvent(e) is FrameworkElement listingElement
                && CanDropDashboardListing(listingElement, e))
            {
                e.Effects = DragDropEffects.Move;
                return;
            }

            if (!CanDropDashboardListingToUngrouped(e))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void DashboardUngroupedDropZone_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            if (FindDashboardListingElementFromDragEvent(e) is FrameworkElement listingElement
                && TryHandleDashboardListingDrop(listingElement.Tag, e, listingElement))
                return;

            switch (e.Data)
            {
                case IDataObject data when data.GetData(typeof(FlashcardSetViewModel)) is FlashcardSetViewModel { Document.GroupId: not null } flashcardSet:
                    vm.RemoveFlashcardSetFromGroup(flashcardSet);
                    e.Handled = true;
                    break;
                case IDataObject data when data.GetData(typeof(MindMapViewModel)) is MindMapViewModel { Document.GroupId: not null } mindMap:
                    vm.RemoveMindMapFromGroup(mindMap);
                    e.Handled = true;
                    break;
                case IDataObject data when data.GetData(typeof(QuizViewModel)) is QuizViewModel { Document.GroupId: not null } quiz:
                    vm.RemoveQuizFromGroup(quiz);
                    e.Handled = true;
                    break;
            }
        }

        private static bool CanDropDashboardListingToUngrouped(DragEventArgs e)
        {
            return e.Data.GetData(typeof(FlashcardSetViewModel)) is FlashcardSetViewModel { Document.GroupId: not null }
                   || e.Data.GetData(typeof(MindMapViewModel)) is MindMapViewModel { Document.GroupId: not null }
                   || e.Data.GetData(typeof(QuizViewModel)) is QuizViewModel { Document.GroupId: not null };
        }

        private static DataObject CreateDashboardListingDataObject(object listing)
        {
            var data = new DataObject();
            switch (listing)
            {
                case FlashcardSetViewModel flashcardSet:
                    data.SetData(typeof(FlashcardSetViewModel), flashcardSet);
                    break;
                case MindMapViewModel mindMap:
                    data.SetData(typeof(MindMapViewModel), mindMap);
                    break;
                case QuizViewModel quiz:
                    data.SetData(typeof(QuizViewModel), quiz);
                    break;
                default:
                    data.SetData(listing.GetType(), listing);
                    break;
            }

            return data;
        }

        private static object? ResolveDashboardListingFromElement(FrameworkElement? element)
        {
            if (element?.Tag is FlashcardSetViewModel or MindMapViewModel or QuizViewModel)
                return element.Tag;

            return element?.DataContext switch
            {
                FlashcardSetViewModel flashcardSet => flashcardSet,
                MindMapViewModel mindMap => mindMap,
                QuizViewModel quiz => quiz,
                _ => null
            };
        }

        private bool TryHandleDashboardListingDrop(object? target, DragEventArgs e, FrameworkElement? targetElement = null)
        {
            var draggedListing = ResolveDashboardDraggedListing(e.Data);
            var placeAfter = targetElement is not null && e.GetPosition(targetElement).X >= targetElement.ActualWidth / 2;
            var handled = TryApplyDashboardListingDrop(draggedListing, target, placeAfter);

            if (handled)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }

            return handled;
        }

        private bool TryApplyDashboardListingDrop(object? draggedListing, object? target, bool placeAfter)
        {
            if (DataContext is not MainViewModel vm)
                return false;

            return target switch
            {
                FlashcardSetViewModel flashcardTarget
                    when draggedListing is FlashcardSetViewModel dragged
                         && !ReferenceEquals(dragged, flashcardTarget)
                    => (dragged.Document.GroupId.HasValue && dragged.Document.GroupId == flashcardTarget.Document.GroupId
                        ? vm.TryReorderFlashcardSetsWithinGroup(dragged, flashcardTarget, placeAfter)
                        : vm.TryGroupFlashcardSets(dragged, flashcardTarget)) || true,
                MindMapViewModel mindMapTarget
                    when draggedListing is MindMapViewModel dragged
                         && !ReferenceEquals(dragged, mindMapTarget)
                    => (dragged.Document.GroupId.HasValue && dragged.Document.GroupId == mindMapTarget.Document.GroupId
                        ? vm.TryReorderMindMapsWithinGroup(dragged, mindMapTarget, placeAfter)
                        : vm.TryGroupMindMaps(dragged, mindMapTarget)) || true,
                QuizViewModel quizTarget
                    when draggedListing is QuizViewModel dragged
                         && !ReferenceEquals(dragged, quizTarget)
                    => (dragged.Document.GroupId.HasValue && dragged.Document.GroupId == quizTarget.Document.GroupId
                        ? vm.TryReorderQuizzesWithinGroup(dragged, quizTarget, placeAfter)
                        : vm.TryGroupQuizzes(dragged, quizTarget)) || true,
                _ => false
            };
        }

        private static object? ResolveDashboardDraggedListing(IDataObject data)
        {
            return data.GetData(typeof(FlashcardSetViewModel))
                   ?? data.GetData(typeof(MindMapViewModel))
                   ?? data.GetData(typeof(QuizViewModel));
        }

        private FrameworkElement? FindDashboardListingElementFromDragEvent(DragEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(this, e.GetPosition(this))?.VisualHit;
            return FindDashboardListingElement(hit)
                   ?? FindDashboardListingElement(e.OriginalSource as DependencyObject);
        }

        private static FrameworkElement? FindDashboardListingElement(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement { Tag: FlashcardSetViewModel or MindMapViewModel or QuizViewModel } element)
                    return element;

                source = GetElementParent(source);
            }

            return null;
        }

        private static bool CanDropDashboardListingOnGroup(object sender, DragEventArgs e)
        {
            return sender switch
            {
                FrameworkElement { Tag: FlashcardSetGroupViewModel target }
                    => e.Data.GetData(typeof(FlashcardSetViewModel)) is FlashcardSetViewModel dragged
                       && dragged.Document.GroupId != target.GroupId,
                FrameworkElement { Tag: MindMapGroupViewModel target }
                    => e.Data.GetData(typeof(MindMapViewModel)) is MindMapViewModel dragged
                       && dragged.Document.GroupId != target.GroupId,
                FrameworkElement { Tag: QuizGroupViewModel target }
                    => e.Data.GetData(typeof(QuizViewModel)) is QuizViewModel dragged
                       && dragged.Document.GroupId != target.GroupId,
                _ => false
            };
        }

        private void MoveDashboardGroupUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || sender is not Button { Tag: object group })
                return;

            switch (group)
            {
                case FlashcardSetGroupViewModel flashcardGroup:
                    vm.MoveFlashcardSetGroupUp(flashcardGroup);
                    break;
                case MindMapGroupViewModel mindMapGroup:
                    vm.MoveMindMapGroupUp(mindMapGroup);
                    break;
                case QuizGroupViewModel quizGroup:
                    vm.MoveQuizGroupUp(quizGroup);
                    break;
            }
        }

        private void MoveDashboardGroupDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || sender is not Button { Tag: object group })
                return;

            switch (group)
            {
                case FlashcardSetGroupViewModel flashcardGroup:
                    vm.MoveFlashcardSetGroupDown(flashcardGroup);
                    break;
                case MindMapGroupViewModel mindMapGroup:
                    vm.MoveMindMapGroupDown(mindMapGroup);
                    break;
                case QuizGroupViewModel quizGroup:
                    vm.MoveQuizGroupDown(quizGroup);
                    break;
            }
        }

        private void DashboardGroupColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { ContextMenu: not null } button)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void DashboardGroupMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { ContextMenu: not null } button)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void DashboardGroupColorMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string colorHex } menuItem || DataContext is not MainViewModel vm)
                return;

            var group = ResolveDashboardGroupFromMenuItem(menuItem);
            switch (group)
            {
                case FlashcardSetGroupViewModel flashcardGroup:
                    vm.SetFlashcardSetGroupBackgroundColor(flashcardGroup, colorHex);
                    break;
                case MindMapGroupViewModel mindMapGroup:
                    vm.SetMindMapGroupBackgroundColor(mindMapGroup, colorHex);
                    break;
                case QuizGroupViewModel quizGroup:
                    vm.SetQuizGroupBackgroundColor(quizGroup, colorHex);
                    break;
            }
        }

        private void RenameDashboardGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || DataContext is not MainViewModel vm)
                return;

            var group = ResolveDashboardGroupFromMenuItem(menuItem);
            var currentName = group switch
            {
                FlashcardSetGroupViewModel flashcardGroup => flashcardGroup.Name,
                MindMapGroupViewModel mindMapGroup => mindMapGroup.Name,
                QuizGroupViewModel quizGroup => quizGroup.Name,
                _ => null
            };

            if (currentName is null)
                return;

            var dialog = new SimpleInputDialog(
                LocalizationService.GetString("RenameGroup"),
                LocalizationService.GetString("RenameGroupPrompt"),
                currentName)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            switch (group)
            {
                case FlashcardSetGroupViewModel flashcardGroup:
                    vm.RenameFlashcardSetGroup(flashcardGroup, dialog.InputText);
                    break;
                case MindMapGroupViewModel mindMapGroup:
                    vm.RenameMindMapGroup(mindMapGroup, dialog.InputText);
                    break;
                case QuizGroupViewModel quizGroup:
                    vm.RenameQuizGroup(quizGroup, dialog.InputText);
                    break;
            }
        }

        private void DisbandDashboardGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || DataContext is not MainViewModel vm)
                return;

            var group = ResolveDashboardGroupFromMenuItem(menuItem);
            if (group is null)
                return;

            var dialog = new GroupDisbandConfirmationDialog(
                LocalizationService.GetString("DisbandGroup"),
                LocalizationService.GetString("DisbandGroupPrompt"),
                LocalizationService.GetString("KeepItemsUngrouped"),
                LocalizationService.GetString("DeleteGroupItems"))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.SelectedChoice == GroupDisbandChoice.Cancel)
                return;

            var deleteItems = dialog.SelectedChoice == GroupDisbandChoice.DeleteNotes;
            switch (group)
            {
                case FlashcardSetGroupViewModel flashcardGroup:
                    vm.DisbandFlashcardSetGroup(flashcardGroup, deleteItems);
                    break;
                case MindMapGroupViewModel mindMapGroup:
                    vm.DisbandMindMapGroup(mindMapGroup, deleteItems);
                    break;
                case QuizGroupViewModel quizGroup:
                    vm.DisbandQuizGroup(quizGroup, deleteItems);
                    break;
            }
        }

        private static object? ResolveDashboardGroupFromMenuItem(MenuItem menuItem)
        {
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

            return (contextMenu?.PlacementTarget as FrameworkElement)?.DataContext
                ?? (contextMenu?.PlacementTarget as Button)?.Tag;
        }

        private static bool CanDropDashboardListing(object sender, DragEventArgs e)
        {
            return sender switch
            {
                FrameworkElement { Tag: FlashcardSetViewModel target }
                    => e.Data.GetData(typeof(FlashcardSetViewModel)) is FlashcardSetViewModel dragged && !ReferenceEquals(dragged, target),
                FrameworkElement { Tag: MindMapViewModel target }
                    => e.Data.GetData(typeof(MindMapViewModel)) is MindMapViewModel dragged && !ReferenceEquals(dragged, target),
                FrameworkElement { Tag: QuizViewModel target }
                    => e.Data.GetData(typeof(QuizViewModel)) is QuizViewModel dragged && !ReferenceEquals(dragged, target),
                _ => false
            };
        }

        private void SetDashboardListingDropVisual(DependencyObject? source, bool isActive)
        {
            if (source is null)
                return;

            var border = FindDashboardListingCardBorder(source);
            if (border is null)
                return;

            if (isActive)
            {
                if (!_dashboardDropOriginalBorderBrushes.ContainsKey(border))
                {
                    _dashboardDropOriginalBorderBrushes[border] = border.BorderBrush;
                    _dashboardDropOriginalBorderThicknesses[border] = border.BorderThickness;
                }

                border.BorderBrush = GetDashboardDropBrush();
                AnimateBorderThickness(border, new Thickness(2), 150);
                return;
            }

            if (!_dashboardDropOriginalBorderBrushes.TryGetValue(border, out var originalBrush)
                || !_dashboardDropOriginalBorderThicknesses.TryGetValue(border, out var originalThickness))
                return;

            border.BorderBrush = originalBrush;
            AnimateBorderThickness(border, originalThickness, 150);
            _dashboardDropOriginalBorderBrushes.Remove(border);
            _dashboardDropOriginalBorderThicknesses.Remove(border);
        }

        private Brush GetDashboardDropBrush()
        {
            return TryFindResource("NoteCardSelectionBorder") as Brush
                ?? TryFindResource("MindMapSelectedBorderBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(74, 110, 224));
        }

        private static Border? FindDashboardListingCardBorder(DependencyObject source)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
            {
                var child = VisualTreeHelper.GetChild(source, i);
                if (child is Border border
                    && border.BorderThickness.Left > 0
                    && border.CornerRadius.TopLeft >= 10
                    && border.Padding.Left >= 8)
                    return border;

                var nested = FindDashboardListingCardBorder(child);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static void AnimateBorderThickness(Border border, Thickness to, int durationMs)
        {
            border.BeginAnimation(Border.BorderThicknessProperty, new ThicknessAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void DashboardGroupedListingCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            var (scale, translate) = EnsureDashboardGroupedListingTransforms(border);
            var shadow = EnsureDashboardGroupedListingShadow(border);
            border.Opacity = 0;
            scale.ScaleX = 0.88;
            scale.ScaleY = 0.88;
            translate.Y = 0;
            shadow.BlurRadius = 8;
            shadow.Opacity = 0.08;

            var popEasing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };
            AnimateDouble(border, UIElement.OpacityProperty, 1, 250);
            AnimateDouble(scale, ScaleTransform.ScaleXProperty, 1, 320, popEasing);
            AnimateDouble(scale, ScaleTransform.ScaleYProperty, 1, 320, popEasing);
        }

        private void DashboardGroupedListingCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Border border)
                return;

            var (scale, _) = EnsureDashboardGroupedListingTransforms(border);
            var shadow = EnsureDashboardGroupedListingShadow(border);
            AnimateDouble(scale, ScaleTransform.ScaleXProperty, 1.02, 180);
            AnimateDouble(scale, ScaleTransform.ScaleYProperty, 1.02, 180);
            AnimateDouble(shadow, DropShadowEffect.BlurRadiusProperty, 14, 180);
            AnimateDouble(shadow, DropShadowEffect.OpacityProperty, 0.18, 180);
        }

        private void DashboardGroupedListingCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Border border)
                return;

            var (scale, _) = EnsureDashboardGroupedListingTransforms(border);
            var shadow = EnsureDashboardGroupedListingShadow(border);
            AnimateDouble(scale, ScaleTransform.ScaleXProperty, 1, 200);
            AnimateDouble(scale, ScaleTransform.ScaleYProperty, 1, 200);
            AnimateDouble(shadow, DropShadowEffect.BlurRadiusProperty, 8, 200);
            AnimateDouble(shadow, DropShadowEffect.OpacityProperty, 0.08, 200);
        }

        private static (ScaleTransform Scale, TranslateTransform Translate) EnsureDashboardGroupedListingTransforms(Border border)
        {
            if (border.RenderTransform is TransformGroup existingGroup)
            {
                var existingScale = existingGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
                var existingTranslate = existingGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (existingScale is not null && existingTranslate is not null)
                    return (existingScale, existingTranslate);
            }

            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            border.RenderTransformOrigin = new Point(0.5, 0.5);
            border.RenderTransform = group;
            return (scale, translate);
        }

        private static DropShadowEffect EnsureDashboardGroupedListingShadow(Border border)
        {
            if (border.Effect is DropShadowEffect shadow
                && border.ReadLocalValue(UIElement.EffectProperty) != DependencyProperty.UnsetValue)
                return shadow;

            shadow = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.08,
                BlurRadius = 8,
                ShadowDepth = 1,
                Direction = 270
            };
            border.Effect = shadow;
            return shadow;
        }

        private static void AnimateDouble(IAnimatable target, DependencyProperty property, double to, int durationMs, IEasingFunction? easingFunction = null)
        {
            target.BeginAnimation(property, new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easingFunction ?? new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private static bool IsWithinListingContextMenuButton(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Button button && string.Equals(button.Name, "ListingContextMenuButton", StringComparison.Ordinal))
                    return true;

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private static FrameworkElement? FindAncestorDashboardListingWithContextMenu(DependencyObject source)
        {
            var current = GetElementParent(source);
            while (current != null)
            {
                if (current is FrameworkElement { ContextMenu: not null, Tag: FlashcardSetViewModel or MindMapViewModel or QuizViewModel } element)
                    return element;

                current = GetElementParent(current);
            }

            return null;
        }

        private static DependencyObject? GetElementParent(DependencyObject source)
        {
            return source is FrameworkElement frameworkElement && frameworkElement.Parent is not null
                ? frameworkElement.Parent
                : VisualTreeHelper.GetParent(source);
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

            var dialog = new GroupDisbandConfirmationDialog(
                LocalizationService.GetString("DisbandGroup"),
                LocalizationService.GetString("DisbandGroupPrompt"))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.SelectedChoice == GroupDisbandChoice.Cancel)
                return;

            vm.DisbandGroup(group, deleteNotes: dialog.SelectedChoice == GroupDisbandChoice.DeleteNotes);
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

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseTopSearchPanel();
            if (SortNotesPopupElement != null)
                SortNotesPopupElement.IsOpen = false;
            TagsFilterPopup.IsOpen = false;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = LocalizationService.GetString("SupportedFormats"),
                Title = LocalizationService.GetString("SelectFilesToImport")
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            try
            {
                int importedCount = 0;

                if (DataContext is MainViewModel vm)
                {
                    foreach (var filePath in openFileDialog.FileNames)
                    {
                        try
                        {
                            string content = File.ReadAllText(filePath);
                            if (string.IsNullOrWhiteSpace(content))
                                continue;

                            // Extract filename without extension as the title
                            string fileName = Path.GetFileNameWithoutExtension(filePath);

                            // Create a new note from the imported content
                            var newDocument = new NoteDocument
                            {
                                Title = fileName,
                                Content = content,
                                Tags = new List<string>(),
                                FontFamily = "Segoe UI",
                                FontSize = 14
                            };

                            // Add to view model
                            vm.AddNoteFromDocument(newDocument);
                            importedCount++;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"{LocalizationService.GetString("ImportError")}\n{Path.GetFileName(filePath)}\n\n{ex.Message}",
                                LocalizationService.GetString("Error"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }

                    if (importedCount > 0)
                    {
                        string message = string.Format(
                            LocalizationService.GetString("ImportSuccess"),
                            importedCount);
                        MessageBox.Show(
                            message,
                            LocalizationService.GetString("ImportNotes"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{LocalizationService.GetString("ImportError")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private MindMapViewModel? GetMindMapFromMenuSender(object sender)
        {
            if (sender is not MenuItem menuItem) return null;
            var contextMenu = menuItem.Parent as ContextMenu;
            var button = contextMenu?.PlacementTarget as Button;
            return button?.Tag as MindMapViewModel;
        }

        private MindMapGroupViewModel? GetMindMapGroupFromMenuSender(object sender)
        {
            if (sender is not MenuItem menuItem) return null;
            var contextMenu = menuItem.Parent as ContextMenu;
            var button = contextMenu?.PlacementTarget as Button;
            return button?.Tag as MindMapGroupViewModel;
        }

        private void RenameMindMapGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var group = GetMindMapGroupFromMenuSender(sender);
            if (group is null) return;
            if (DataContext is not MainViewModel vm) return;

            var dialog = new SimpleInputDialog("Rename set", "Enter a new name:", group.Name) { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
                vm.RenameMindMapGroup(group, dialog.InputText!);
        }

        private void DeleteMindMapGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var group = GetMindMapGroupFromMenuSender(sender);
            if (group is null) return;
            if (DataContext is not MainViewModel vm) return;

            var dialog = new DeleteConfirmationDialog(
                "Delete set",
                $"Delete the set \"{group.Name}\"? Mind maps inside will not be deleted.")
            { Owner = this };
            if (dialog.ShowDialog() == true)
                vm.DeleteMindMapGroup(group);
        }

    }
}
