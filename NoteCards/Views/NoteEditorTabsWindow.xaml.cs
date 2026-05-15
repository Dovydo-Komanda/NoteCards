using NoteCards.Models;
using NoteCards.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NoteCards;

public partial class NoteEditorTabsWindow : Window
{
    private sealed class EditorTabState
    {
        public required Guid DocumentId { get; init; }
        public required NoteEditorWindow Editor { get; init; }
        public required NoteCardViewModel NoteViewModel { get; init; }
        public required TabItem TabItem { get; init; }
        public required TextBlock TitleTextBlock { get; init; }
        public required Button CloseButton { get; init; }
        public required Action<NoteDocument> AutoSaveHandler { get; init; }
        public required RoutedEventHandler CloseClickHandler { get; init; }
    }

    private readonly Dictionary<Guid, EditorTabState> _tabsByDocumentId = new();
    private bool _isWindowClosing;

    public NoteEditorTabsWindow()
    {
        InitializeComponent();
        NoteCards.Services.WindowThemeService.Register(this);
    }

    public void OpenOrFocusNote(NoteCardViewModel noteViewModel, object? sharedDataContext)
    {
        var documentId = noteViewModel.Document.Id;
        if (_tabsByDocumentId.TryGetValue(documentId, out var existingState))
        {
            EditorTabs.SelectedItem = existingState.TabItem;
            existingState.Editor.Focus();
            return;
        }

        var editor = new NoteEditorWindow();
        editor.EnableTabMode();
        editor.DataContext = sharedDataContext;
        editor.LoadFromDocument(noteViewModel.Document);
        editor.SetCurrentDocument(noteViewModel.Document);

        var hostedContent = editor.DetachEditorContentForHosting();
        if (hostedContent == null)
            return;

        var titleText = new TextBlock
        {
            Text = GetTabHeaderText(noteViewModel),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = TryFindResource("TextColor") as Brush ?? Brushes.Black
        };

        var closeButton = new Button
        {
            Content = "×",
            Width = 20,
            Height = 20,
            FontSize = 12,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = TryFindResource("TextColor") as Brush ?? Brushes.Black
        };

        var headerPanel = new DockPanel
        {
            LastChildFill = false
        };
        headerPanel.Children.Add(titleText);
        headerPanel.Children.Add(closeButton);

        var tabItem = new TabItem
        {
            Header = headerPanel,
            Content = hostedContent
        };

        Action<NoteDocument> autoSaveHandler = _ =>
        {
            noteViewModel.NotifyContentChanged();
            titleText.Text = GetTabHeaderText(noteViewModel);
        };
        editor.DocumentAutoSaved += autoSaveHandler;
        editor.CloseRequested += OnEditorCloseRequested;

        RoutedEventHandler closeClickHandler = (s, e) =>
        {
            e.Handled = true;
            CloseTab(documentId);
        };
        closeButton.Click += closeClickHandler;

        var state = new EditorTabState
        {
            DocumentId = documentId,
            Editor = editor,
            NoteViewModel = noteViewModel,
            TabItem = tabItem,
            TitleTextBlock = titleText,
            CloseButton = closeButton,
            AutoSaveHandler = autoSaveHandler,
            CloseClickHandler = closeClickHandler
        };

        _tabsByDocumentId[documentId] = state;

        EditorTabs.Items.Add(tabItem);
        EditorTabs.SelectedItem = tabItem;
    }

    private static string GetTabHeaderText(NoteCardViewModel noteViewModel)
    {
        var title = noteViewModel.Document.Title;
        return string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
    }

    private void OnEditorCloseRequested(NoteEditorWindow editor)
    {
        foreach (var state in _tabsByDocumentId.Values)
        {
            if (ReferenceEquals(state.Editor, editor))
            {
                CloseTab(state.DocumentId, askConfirmation: false);
                break;
            }
        }
    }

    private bool CloseTab(Guid documentId, bool askConfirmation = true)
    {
        if (!_tabsByDocumentId.TryGetValue(documentId, out var state))
            return true;

        if (askConfirmation && !state.Editor.ConfirmCloseIfNeeded())
            return false;

        state.Editor.DocumentAutoSaved -= state.AutoSaveHandler;
        state.Editor.CloseRequested -= OnEditorCloseRequested;
        state.CloseButton.Click -= state.CloseClickHandler;
        state.Editor.DisposeHostedEditor();

        state.TabItem.Content = null;
        EditorTabs.Items.Remove(state.TabItem);
        _tabsByDocumentId.Remove(documentId);

        if (!_isWindowClosing && EditorTabs.Items.Count == 0)
            Close();

        return true;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _isWindowClosing = true;

        foreach (var state in _tabsByDocumentId.Values.ToList())
        {
            if (state.Editor.ConfirmCloseIfNeeded())
                continue;

            e.Cancel = true;
            _isWindowClosing = false;
            return;
        }

        foreach (var documentId in _tabsByDocumentId.Keys.ToList())
            CloseTab(documentId, askConfirmation: false);
    }
}
