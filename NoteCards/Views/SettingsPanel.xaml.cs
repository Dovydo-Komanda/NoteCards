using NoteCards.Localization;
using NoteCards.Models;
using NoteCards.Services;
using NoteCards.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class SettingsPanel : UserControl
    {
        private const int OverlayAnimationMs = 180;
        private const int PanelAnimationMs = 220;
        private const double PanelOffsetY = 14;

        private bool _isClosing;
        private bool _isApplyingSettings;
        private string _lastSelectedFlashcardModelKey = "Qwen3.5-0.8B";

        public SettingsPanel()
        {
            InitializeComponent();
            LocalizationProvider.Instance.PropertyChanged += LocalizationProvider_PropertyChanged;
            Unloaded += (_, _) => LocalizationProvider.Instance.PropertyChanged -= LocalizationProvider_PropertyChanged;
        }

        private void LocalizationProvider_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, "Item[]", StringComparison.Ordinal))
                return;

            var machineMemoryBytes = BundledModelHostService.GetTotalPhysicalMemoryBytesForCurrentMachine();
            RefreshFlashcardModelOptions(machineMemoryBytes);

            var selectedKey = GetSelectedFlashcardModelKey() ?? _lastSelectedFlashcardModelKey;
            UpdateFlashcardModelWarning(selectedKey, machineMemoryBytes);
        }

        public void ShowAnimated()
        {
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

            var openOverlay = new DoubleAnimation(0, 1, overlayDuration)
            {
                EasingFunction = easeOut
            };
            var openPanelOpacity = new DoubleAnimation(0, 1, panelDuration)
            {
                EasingFunction = easeOut
            };
            var openPanelShift = new DoubleAnimation(PanelOffsetY, 0, panelDuration)
            {
                EasingFunction = easeOut
            };
            openPanelShift.Completed += (_, _) =>
            {
                OverlayRoot.Opacity = 1;
                PanelCard.Opacity = 1;
                translate.Y = 0;
            };

            OverlayRoot.BeginAnimation(OpacityProperty, openOverlay);
            PanelCard.BeginAnimation(OpacityProperty, openPanelOpacity);
            translate.BeginAnimation(TranslateTransform.YProperty, openPanelShift);

            var settings = AppSettingsService.Load();
            var machineMemoryBytes = BundledModelHostService.GetTotalPhysicalMemoryBytesForCurrentMachine();
            var recommendedModelKey = BundledModelHostService.GetRecommendedFlashcardModelKey(machineMemoryBytes);
            var flashcardModelBox = FindName("FlashcardModelBox") as ComboBox;

            _isApplyingSettings = true;
            EnableScrollbarCheckBox.IsChecked = settings.EnableScrollbar;
            EnableAutoSaveCheckBox.IsChecked = settings.EnableAutoSave;
            if (FindName("FlashcardFlipSpeedSlider") is Slider flipSpeedSlider)
                flipSpeedSlider.Value = settings.FlashcardFlipDelayMilliseconds;
            RefreshFlashcardModelOptions(machineMemoryBytes);
            if (flashcardModelBox is not null)
                SelectComboBoxItemByTag(flashcardModelBox, settings.FlashcardModelKey, recommendedModelKey);
            _lastSelectedFlashcardModelKey = GetSelectedFlashcardModelKey() ?? recommendedModelKey;
            UpdateFlashcardModelWarning(_lastSelectedFlashcardModelKey, machineMemoryBytes);

            var selectedViewMode = string.Equals(settings.DefaultViewMode, "List", StringComparison.OrdinalIgnoreCase)
                ? "List"
                : "Grid";
            foreach (var comboBoxItem in ViewModeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(comboBoxItem.Tag?.ToString(), selectedViewMode, StringComparison.OrdinalIgnoreCase))
                {
                    ViewModeBox.SelectedItem = comboBoxItem;
                    break;
                }
            }
            _isApplyingSettings = false;
        }

        private void RefreshFlashcardModelOptions(long machineMemoryBytes)
        {
            var flashcardModelBox = FindName("FlashcardModelBox") as ComboBox;
            if (flashcardModelBox is null)
                return;

            foreach (var comboBoxItem in flashcardModelBox.Items.OfType<ComboBoxItem>())
            {
                var key = comboBoxItem.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var isCompatible = BundledModelHostService.IsFlashcardModelCompatibleWithMemory(key, machineMemoryBytes);
                comboBoxItem.Content = BundledModelHostService.GetFlashcardModelDisplayLabel(key, includeWarningPrefix: true, isCompatible: isCompatible);
            }

            if (FindName("FlashcardModelContextMenu") is ContextMenu flashcardModelContextMenu)
            {
                foreach (var menuItem in flashcardModelContextMenu.Items.OfType<MenuItem>())
                {
                    var key = menuItem.Tag?.ToString();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var isCompatible = BundledModelHostService.IsFlashcardModelCompatibleWithMemory(key, machineMemoryBytes);
                    menuItem.Header = BundledModelHostService.GetFlashcardModelDisplayLabel(key, includeWarningPrefix: true, isCompatible: isCompatible);
                }
            }
        }

        private string? GetSelectedFlashcardModelKey()
        {
            return FindName("FlashcardModelBox") is ComboBox flashcardModelBox && flashcardModelBox.SelectedItem is ComboBoxItem item
                ? item.Tag?.ToString()
                : null;
        }

        private void UpdateFlashcardModelWarning(string selectedKey, long machineMemoryBytes)
        {
            var warningTextBlock = FindName("FlashcardModelWarningText") as TextBlock;
            if (warningTextBlock is null)
                return;

            var isCompatible = BundledModelHostService.IsFlashcardModelCompatibleWithMemory(selectedKey, machineMemoryBytes);
            if (isCompatible)
            {
                warningTextBlock.Visibility = Visibility.Collapsed;
                warningTextBlock.Text = string.Empty;
                return;
            }

            warningTextBlock.Text = string.Format(
                LocalizationService.GetString("FlashcardModelWarningText"),
                BundledModelHostService.GetFlashcardModelDisplayName(selectedKey));
            warningTextBlock.Visibility = Visibility.Visible;
        }

        private static void SelectComboBoxItemByTag(ComboBox comboBox, string? tag, string fallbackTag)
        {
            var selectedTag = string.IsNullOrWhiteSpace(tag) ? fallbackTag : tag;

            foreach (var comboBoxItem in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(comboBoxItem.Tag?.ToString(), selectedTag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            foreach (var comboBoxItem in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(comboBoxItem.Tag?.ToString(), fallbackTag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }
        }

        // auto-save checkbox handlers
        private void EnableAutoSaveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettings)
                return;

            // Enable auto-save in all open editor windows
            foreach (var window in Application.Current.Windows.OfType<NoteEditorWindow>())
            {
                window.SetAutoSaveEnabled(true);
            }

            // Save preference
            var settings = AppSettingsService.Load();
            settings.EnableAutoSave = true;
            AppSettingsService.Save(settings);
        }

        private void EnableAutoSaveCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettings)
                return;

            // Disable auto-save in all open editor windows
            foreach (var window in Application.Current.Windows.OfType<NoteEditorWindow>())
            {
                window.SetAutoSaveEnabled(false);
            }

            // Save preference
            var settings = AppSettingsService.Load();
            settings.EnableAutoSave = false;
            AppSettingsService.Save(settings);
        }

        private void FlashcardModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlashcardModelSelection(sender);
        }

        private void FlashcardModelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            HandleFlashcardModelSelection(sender);
        }

        private void HandleFlashcardModelSelection(object sender)
        {
            if (_isApplyingSettings)
                return;

            var selected = sender switch
            {
                ComboBox comboBox when comboBox.SelectedItem is ComboBoxItem comboItem => comboItem.Tag?.ToString(),
                MenuItem menuItem => menuItem.Tag?.ToString(),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(selected))
                return;

            var machineMemoryBytes = BundledModelHostService.GetTotalPhysicalMemoryBytesForCurrentMachine();
            var isCompatible = BundledModelHostService.IsFlashcardModelCompatibleWithMemory(selected, machineMemoryBytes);
            if (!isCompatible)
            {
                var warning = string.Format(
                    LocalizationService.GetString("FlashcardModelWarningPrompt"),
                    BundledModelHostService.GetFlashcardModelDisplayName(selected));

                var dialog = new ModelCompatibilityWarningDialog(
                    LocalizationService.GetString("FlashcardModelWarningTitle"),
                    warning)
                {
                    Owner = Window.GetWindow(this)
                };

                var result = dialog.ShowDialog();

                if (result != true)
                {
                    if (FindName("FlashcardModelBox") is ComboBox flashcardModelBox)
                    {
                        _isApplyingSettings = true;
                        SelectComboBoxItemByTag(flashcardModelBox, _lastSelectedFlashcardModelKey, _lastSelectedFlashcardModelKey);
                        _isApplyingSettings = false;
                    }

                    UpdateFlashcardModelWarning(_lastSelectedFlashcardModelKey, machineMemoryBytes);
                    return;
                }
            }

            if (FindName("FlashcardModelBox") is ComboBox flashcardModelBox2)
            {
                _isApplyingSettings = true;
                SelectComboBoxItemByTag(flashcardModelBox2, selected, selected);
                _isApplyingSettings = false;
            }

            var settings = AppSettingsService.Load();
            settings.FlashcardModelKey = selected;
            AppSettingsService.Save(settings);

            _lastSelectedFlashcardModelKey = selected;
            UpdateFlashcardModelWarning(selected, machineMemoryBytes);
        }

        public void HideAnimated()
        {
            if (_isClosing || Visibility != Visibility.Visible)
                return;

            _isClosing = true;
            IsHitTestVisible = false;

            var translate = EnsurePanelTranslate();
            var startOverlayOpacity = OverlayRoot.Opacity;
            var startPanelOpacity = PanelCard.Opacity;
            var startY = translate.Y;

            if (startOverlayOpacity <= 0)
                startOverlayOpacity = 1;

            if (startPanelOpacity <= 0)
                startPanelOpacity = 1;

            var overlayDuration = TimeSpan.FromMilliseconds(OverlayAnimationMs);
            var panelDuration = TimeSpan.FromMilliseconds(PanelAnimationMs);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var closeOverlay = new DoubleAnimation(startOverlayOpacity, 0, overlayDuration)
            {
                EasingFunction = easeIn
            };
            var closePanelOpacity = new DoubleAnimation(startPanelOpacity, 0, panelDuration)
            {
                EasingFunction = easeIn
            };
            var closePanelShift = new DoubleAnimation(startY, PanelOffsetY, panelDuration)
            {
                EasingFunction = easeIn
            };

            closePanelShift.Completed += (_, _) =>
            {
                Visibility = Visibility.Collapsed;
                OverlayRoot.Opacity = 0;
                PanelCard.Opacity = 0;
                translate.Y = PanelOffsetY;
                _isClosing = false;
            };

            OverlayRoot.BeginAnimation(OpacityProperty, closeOverlay);
            PanelCard.BeginAnimation(OpacityProperty, closePanelOpacity);
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideAnimated();
        }

        private void OverlayRoot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender)
                HideAnimated();
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ModernInfoDialog(
                LocalizationService.GetString("AppUpdate"),
                LocalizationService.GetString("LatestVersion"))
            {
                Owner = Window.GetWindow(this)
            };

            dialog.ShowDialog();
        }

        private void ResetFactorySettings_Click(object sender, RoutedEventArgs e)
        {
            var confirmDialog = new ResetFactorySettingsDialog
            {
                Owner = Window.GetWindow(this)
            };

            var result = confirmDialog.ShowDialog();

            if (result == true)
            {
                PerformFactoryReset();
            }
        }

        private void PerformFactoryReset()
        {
            try
            {
                // Reset all settings to defaults
                var defaultSettings = new AppSettings
                {
                    FlashcardModelKey = BundledModelHostService.GetRecommendedFlashcardModelKeyForCurrentMachine()
                };

                AppSettingsService.Save(defaultSettings);

                // Apply language change to reflect new settings
                LocalizationService.SetCulture(defaultSettings.Language);

                // Apply theme change
                ThemeManager.SetTheme(defaultSettings.Theme);

                // Reload the settings in this panel to reflect the reset
                HideAnimated();

                // Show confirmation message
                var confirmMessage = new ModernInfoDialog(
                    LocalizationService.GetString("Success"),
                    "Settings have been reset to factory defaults. The app will now restart.")
                {
                    Owner = Window.GetWindow(this)
                };

                confirmMessage.ShowDialog();

                // Restart the application
                System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                var errorDialog = new ModernInfoDialog(
                    LocalizationService.GetString("Error"),
                    $"Failed to reset factory settings: {ex.Message}")
                {
                    Owner = Window.GetWindow(this)
                };

                errorDialog.ShowDialog();
            }
        }

        private void ViewModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingSettings)
                return;

            if (ViewModeBox.SelectedItem is ComboBoxItem item)
            {
                var selected = string.Equals(item.Tag?.ToString(), "List", StringComparison.OrdinalIgnoreCase)
                    ? "List"
                    : "Grid";

                var settings = AppSettingsService.Load();
                settings.DefaultViewMode = selected;
                AppSettingsService.Save(settings);

                if (Application.Current.MainWindow.DataContext is MainViewModel vm)
                {
                    vm.ViewMode = selected;
                }
            }
        }

            }
        }