using NoteCards.Localization;
using NoteCards.Models;
using NoteCards.Services;
using NoteCards.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class SettingsPanel : UserControl
    {
        private sealed class ManagedAiToolItem
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public bool IsEnabled { get; set; }
            public bool IsDownloaded { get; set; }
        }

        private const int OverlayAnimationMs = 180;
        private const int PanelAnimationMs = 220;
        private const double PanelOffsetY = 14;

        private bool _isClosing;
        private bool _isApplyingSettings;
        private bool _isDownloadingAiModel;
        private bool _isDownloadingRuntime;
        private string _lastSelectedFlashcardModelKey = "Qwen3.5-0.8B";
        private readonly ObservableCollection<ManagedAiToolItem> _managedAiTools = new();

        public SettingsPanel()
        {
            InitializeComponent();
            AiToolsListBox.ItemsSource = _managedAiTools;
            LocalizationProvider.Instance.PropertyChanged += LocalizationProvider_PropertyChanged;
            Unloaded += (_, _) => LocalizationProvider.Instance.PropertyChanged -= LocalizationProvider_PropertyChanged;
        }

        private void LocalizationProvider_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, "Item[]", StringComparison.Ordinal))
                return;

            var machineMemoryBytes = BundledModelHostService.GetTotalPhysicalMemoryBytesForCurrentMachine();
            RefreshFlashcardModelOptions(machineMemoryBytes, AppSettingsService.Load());

            var selectedKey = GetSelectedFlashcardModelKey() ?? _lastSelectedFlashcardModelKey;
            UpdateFlashcardModelWarning(selectedKey, machineMemoryBytes);
            UpdateAiToolsStatusText();
            UpdateRuntimeStatusText();
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
            LoadManagedAiTools(settings);
            RefreshFlashcardModelOptions(machineMemoryBytes, settings);
            RefreshManagedAiToolDownloadState();
            UpdateRuntimeStatusText();
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

        private void LoadManagedAiTools(AppSettings settings)
        {
            var supportedTools = BundledModelHostService.GetSupportedFlashcardTools()
                .ToDictionary(tool => tool.Key, StringComparer.OrdinalIgnoreCase);

            var orderedItems = new List<ManagedAiToolItem>();
            foreach (var item in settings.AiTools ?? new List<AiToolSettingsItem>())
            {
                if (string.IsNullOrWhiteSpace(item.Key) || !supportedTools.TryGetValue(item.Key, out var supported))
                    continue;

                orderedItems.Add(new ManagedAiToolItem
                {
                    Key = supported.Key,
                    Name = supported.DisplayName,
                    IsEnabled = item.IsEnabled,
                    IsDownloaded = BundledModelHostService.IsModelDownloaded(supported.Key)
                });
            }

            foreach (var supported in supportedTools.Values)
            {
                if (orderedItems.Any(item => string.Equals(item.Key, supported.Key, StringComparison.OrdinalIgnoreCase)))
                    continue;

                orderedItems.Add(new ManagedAiToolItem
                {
                    Key = supported.Key,
                    Name = supported.DisplayName,
                    IsEnabled = true,
                    IsDownloaded = BundledModelHostService.IsModelDownloaded(supported.Key)
                });
            }

            if (!orderedItems.Any(item => item.IsEnabled) && orderedItems.Count > 0)
                orderedItems[0].IsEnabled = true;

            _managedAiTools.Clear();
            foreach (var item in orderedItems)
                _managedAiTools.Add(item);

            if (_managedAiTools.Count > 0 && AiToolsListBox.SelectedItem is null)
                AiToolsListBox.SelectedIndex = 0;

            UpdateAiToolsStatusText();
            UpdateRuntimeStatusText();
            UpdateAiActionButtons();
        }

        private void PersistManagedAiTools()
        {
            if (!_managedAiTools.Any(tool => tool.IsEnabled) && _managedAiTools.Count > 0)
                _managedAiTools[0].IsEnabled = true;

            var settings = AppSettingsService.Load();
            settings.AiTools = _managedAiTools
                .Select(item => new AiToolSettingsItem
                {
                    Key = item.Key,
                    IsEnabled = item.IsEnabled,
                    IsRemoved = false
                })
                .ToList();

            if (!settings.AiTools.Any(tool => tool.IsEnabled) && settings.AiTools.Count > 0)
                settings.AiTools[0].IsEnabled = true;

            var enabledKeys = BundledModelHostService.GetEnabledFlashcardModelKeys(settings);
            var selectedModelKey = GetSelectedFlashcardModelKey();

            if (string.IsNullOrWhiteSpace(selectedModelKey)
                || !enabledKeys.Any(key => string.Equals(key, selectedModelKey, StringComparison.OrdinalIgnoreCase)))
            {
                selectedModelKey = enabledKeys.FirstOrDefault()
                    ?? BundledModelHostService.GetRecommendedFlashcardModelKeyForCurrentMachine();
            }

            settings.FlashcardModelKey = selectedModelKey;
            AppSettingsService.Save(settings);

            _lastSelectedFlashcardModelKey = selectedModelKey;
            var machineMemoryBytes = BundledModelHostService.GetTotalPhysicalMemoryBytesForCurrentMachine();
            RefreshFlashcardModelOptions(machineMemoryBytes, settings);
            UpdateFlashcardModelWarning(_lastSelectedFlashcardModelKey, machineMemoryBytes);
            UpdateAiToolsStatusText();
            UpdateRuntimeStatusText();
            UpdateAiActionButtons();
        }

        private void UpdateAiToolsStatusText()
        {
            if (AiToolsStatusText is null)
                return;

            if (_isDownloadingAiModel)
                return;

            if (AiToolsListBox.SelectedItem is not ManagedAiToolItem selected)
            {
                AiToolsStatusText.Text = _managedAiTools.Count == 0
                    ? LocalizationService.GetString("AiToolsNoModelsConfigured")
                    : LocalizationService.GetString("AiToolsSelectModelToManage");
                UpdateAiActionButtons();
                return;
            }

            selected.IsDownloaded = BundledModelHostService.IsModelDownloaded(selected.Key);

            AiToolsStatusText.Text = selected.IsDownloaded
                ? string.Format(LocalizationService.GetString("AiToolsModelReadyFormat"), selected.Name)
                : string.Format(LocalizationService.GetString("AiToolsModelNotDownloadedFormat"), selected.Name);

            AiToolsListBox.Items.Refresh();
            UpdateAiActionButtons();
        }

        private void UpdateRuntimeStatusText()
        {
            if (RuntimeStatusText is null)
                return;

            if (_isDownloadingRuntime)
                return;

            RuntimeStatusText.Text = BundledModelHostService.IsRuntimeDownloaded()
                ? LocalizationService.GetString("AiToolsRuntimeReady")
                : LocalizationService.GetString("AiToolsRuntimeNotDownloaded");

            UpdateAiActionButtons();
        }

        private void UpdateAiActionButtons()
        {
            var selected = AiToolsListBox.SelectedItem as ManagedAiToolItem;
            var hasSelection = selected is not null;

            var isSelectedModelDownloaded = false;
            if (selected is not null)
            {
                isSelectedModelDownloaded = BundledModelHostService.IsModelDownloaded(selected.Key);
                selected.IsDownloaded = isSelectedModelDownloaded;
            }

            if (FindName("DownloadSelectedAiToolButton") is Button downloadModelButton)
                downloadModelButton.IsEnabled = hasSelection && !_isDownloadingAiModel && !isSelectedModelDownloaded;

            if (FindName("DeleteSelectedAiToolButton") is Button deleteModelButton)
                deleteModelButton.IsEnabled = hasSelection && !_isDownloadingAiModel && isSelectedModelDownloaded;

            var isRuntimeDownloaded = BundledModelHostService.IsRuntimeDownloaded();

            if (FindName("RuntimeDownloadButton") is Button runtimeDownloadButton)
                runtimeDownloadButton.IsEnabled = !_isDownloadingRuntime && !isRuntimeDownloaded;

            if (FindName("RuntimeDeleteButton") is Button runtimeDeleteButton)
                runtimeDeleteButton.IsEnabled = !_isDownloadingRuntime && isRuntimeDownloaded;
        }

        private void RefreshManagedAiToolDownloadState()
        {
            foreach (var item in _managedAiTools)
                item.IsDownloaded = BundledModelHostService.IsModelDownloaded(item.Key);

            AiToolsListBox.Items.Refresh();
            UpdateAiToolsStatusText();
        }

        private void RefreshFlashcardModelOptions(long machineMemoryBytes, AppSettings settings)
        {
            var flashcardModelBox = FindName("FlashcardModelBox") as ComboBox;
            if (flashcardModelBox is null)
                return;

            _isApplyingSettings = true;
            flashcardModelBox.Items.Clear();

            var enabledKeys = BundledModelHostService.GetEnabledFlashcardModelKeys(settings);
            foreach (var key in enabledKeys)
            {
                var isCompatible = BundledModelHostService.IsFlashcardModelCompatibleWithMemory(key, machineMemoryBytes);
                flashcardModelBox.Items.Add(new ComboBoxItem
                {
                    Tag = key,
                    Content = BundledModelHostService.GetFlashcardModelDisplayLabel(key, includeWarningPrefix: true, isCompatible: isCompatible)
                });
            }

            if (FindName("FlashcardModelContextMenu") is ContextMenu flashcardModelContextMenu)
            {
                flashcardModelContextMenu.Items.Clear();
                foreach (var key in enabledKeys)
                {
                    var isCompatible = BundledModelHostService.IsFlashcardModelCompatibleWithMemory(key, machineMemoryBytes);
                    var menuItem = new MenuItem
                    {
                        Tag = key,
                        Header = BundledModelHostService.GetFlashcardModelDisplayLabel(key, includeWarningPrefix: true, isCompatible: isCompatible)
                    };
                    menuItem.Click += FlashcardModelMenuItem_Click;
                    flashcardModelContextMenu.Items.Add(menuItem);
                }
            }

            SelectComboBoxItemByTag(flashcardModelBox, settings.FlashcardModelKey, enabledKeys.FirstOrDefault() ?? _lastSelectedFlashcardModelKey);
            _isApplyingSettings = false;

            return;
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

        private void AiToolEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettings)
                return;

            if (sender is not CheckBox checkBox || checkBox.Tag is not string key)
                return;

            var item = _managedAiTools.FirstOrDefault(tool => string.Equals(tool.Key, key, StringComparison.OrdinalIgnoreCase));
            if (item is null)
                return;

            item.IsEnabled = checkBox.IsChecked == true;
            PersistManagedAiTools();
        }

        private void MoveAiToolUp_Click(object sender, RoutedEventArgs e)
        {
            if (AiToolsListBox.SelectedItem is not ManagedAiToolItem selected)
                return;

            var currentIndex = _managedAiTools.IndexOf(selected);
            if (currentIndex <= 0)
                return;

            _managedAiTools.Move(currentIndex, currentIndex - 1);
            AiToolsListBox.SelectedItem = selected;
            PersistManagedAiTools();
        }

        private void MoveAiToolDown_Click(object sender, RoutedEventArgs e)
        {
            if (AiToolsListBox.SelectedItem is not ManagedAiToolItem selected)
                return;

            var currentIndex = _managedAiTools.IndexOf(selected);
            if (currentIndex < 0 || currentIndex >= _managedAiTools.Count - 1)
                return;

            _managedAiTools.Move(currentIndex, currentIndex + 1);
            AiToolsListBox.SelectedItem = selected;
            PersistManagedAiTools();
        }

        private void DeleteSelectedAiTool_Click(object sender, RoutedEventArgs e)
        {
            if (AiToolsListBox.SelectedItem is not ManagedAiToolItem selected)
                return;

            var deleteDialog = new DeleteConfirmationDialog(
                LocalizationService.GetString("AiToolsDeleteTitle"),
                string.Format(LocalizationService.GetString("AiToolsDeletePromptFormat"), selected.Name))
            {
                Owner = Window.GetWindow(this)
            };

            if (deleteDialog.ShowDialog() != true)
                return;

            BundledModelHostService.DeleteModelArtifacts(selected.Key);

            selected.IsDownloaded = false;

            PersistManagedAiTools();
            RefreshManagedAiToolDownloadState();
        }

        private async void DownloadSelectedAiTool_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingAiModel)
                return;

            if (AiToolsListBox.SelectedItem is not ManagedAiToolItem selected)
                return;

            _isDownloadingAiModel = true;
            AiToolsStatusText.Text = string.Format(LocalizationService.GetString("AiToolsDownloadStartingFormat"), selected.Name);
            UpdateAiActionButtons();

            try
            {
                var progress = new Progress<BundledModelHostService.FlashcardProgress>(p =>
                {
                    if (p.Percent.HasValue)
                        AiToolsStatusText.Text = string.Format(LocalizationService.GetString("AiToolsDownloadingPercentFormat"), selected.Name, p.Percent.Value);
                    else
                        AiToolsStatusText.Text = string.Format(LocalizationService.GetString("AiToolsDownloadPreparingFormat"), selected.Name);
                });

                await BundledModelHostService.Instance.EnsureModelAvailableAsync(selected.Key, progress);
                RefreshManagedAiToolDownloadState();
                AiToolsStatusText.Text = string.Format(LocalizationService.GetString("AiToolsDownloadSuccessFormat"), selected.Name);
            }
            catch (Exception ex)
            {
                AiToolsStatusText.Text = string.Format(LocalizationService.GetString("AiToolsDownloadFailedFormat"), selected.Name);

                var dialog = new ModernInfoDialog(
                    LocalizationService.GetString("Error"),
                    string.Format(LocalizationService.GetString("AiToolsDownloadErrorDialogFormat"), selected.Name, ex.Message))
                {
                    Owner = Window.GetWindow(this)
                };

                dialog.ShowDialog();
            }
            finally
            {
                _isDownloadingAiModel = false;
                UpdateAiToolsStatusText();
                UpdateAiActionButtons();
            }
        }

        private void AiToolsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAiToolsStatusText();
            UpdateAiActionButtons();
        }

        private async void DownloadRuntime_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingRuntime)
                return;

            _isDownloadingRuntime = true;
            RuntimeStatusText.Text = LocalizationService.GetString("AiToolsRuntimeDownloadStarting");
            UpdateAiActionButtons();

            try
            {
                var progress = new Progress<BundledModelHostService.FlashcardProgress>(p =>
                {
                    if (p.Percent.HasValue)
                        RuntimeStatusText.Text = string.Format(LocalizationService.GetString("AiToolsRuntimeDownloadingPercentFormat"), p.Percent.Value);
                    else
                        RuntimeStatusText.Text = LocalizationService.GetString("AiToolsRuntimeDownloadPreparing");
                });

                await BundledModelHostService.Instance.EnsureRuntimeAvailableAsync(progress);
                RuntimeStatusText.Text = LocalizationService.GetString("AiToolsRuntimeDownloadSuccess");
            }
            catch (Exception ex)
            {
                RuntimeStatusText.Text = LocalizationService.GetString("AiToolsRuntimeDownloadFailed");

                var dialog = new ModernInfoDialog(
                    LocalizationService.GetString("Error"),
                    ex.Message)
                {
                    Owner = Window.GetWindow(this)
                };

                dialog.ShowDialog();
            }
            finally
            {
                _isDownloadingRuntime = false;
                UpdateRuntimeStatusText();
                UpdateAiActionButtons();
            }
        }

        private void DeleteRuntime_Click(object sender, RoutedEventArgs e)
        {
            var deleteDialog = new DeleteConfirmationDialog(
                LocalizationService.GetString("AiToolsRuntimeDeleteTitle"),
                LocalizationService.GetString("AiToolsRuntimeDeletePrompt"))
            {
                Owner = Window.GetWindow(this)
            };

            if (deleteDialog.ShowDialog() != true)
                return;

            BundledModelHostService.DeleteRuntimeArtifacts();

            UpdateRuntimeStatusText();
        }

        private void OpenRuntimeFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var runtimeDir = BundledModelHostService.GetRuntimeDirectoryPath();
                Directory.CreateDirectory(runtimeDir);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = runtimeDir,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void OpenAiToolsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var modelsPath = BundledModelHostService.GetModelsDirectoryPath();
                Directory.CreateDirectory(modelsPath);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = modelsPath,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
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
