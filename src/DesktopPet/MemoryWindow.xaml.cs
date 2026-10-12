using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPet.Core;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace DesktopPet;

public partial class MemoryWindow : Window
{
    private readonly ChatRuntime _runtime;
    private string _baseline = ChatMemoryStore.EmptyDocument;
    private MemoryDocument _document = MemoryDocument.Parse(ChatMemoryStore.EmptyDocument);
    private MemoryNote? _selected;
    private bool _newNote;
    private bool _updating = true;
    private bool _loaded;
    private bool _closed;
    private bool _forceClose;
    private bool _editorDirty;
    private int? _removedNoteId;
    private int? _insertedNoteId;

    public MemoryWindow(ChatRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        InitializeComponent();
        RecordingCheckBox.IsChecked = _runtime.MemoryRecordingEnabled;
        var sectionText = DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox));
        sectionText.AddValueChanged(SectionBox, OnSectionChanged);
        _runtime.StateChanged += OnRuntimeStateChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            _runtime.StateChanged -= OnRuntimeStateChanged;
            sectionText.RemoveValueChanged(SectionBox, OnSectionChanged);
        };
        PreviewKeyDown += OnKeyDown;
        _updating = false;
        UpdateRecordingHelp();
        LoadDocument();
    }

    private bool IsDirty => _loaded && (_document.Source != _baseline || _editorDirty);

    public void CloseForExit()
    {
        _forceClose = true;
        Close();
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(error ? "#98435B" : "#756A7D"));
    }

    private void LoadDocument()
    {
        try
        {
            _baseline = _runtime.LoadMemoryDocument();
            _document = MemoryDocument.Parse(_baseline);
            _loaded = true;
            SelectNote(null);
            RefreshList();
            SetStatus("记忆已读取。编辑和保存都在本机完成。");
        }
        catch (Exception error) when (IsFileError(error))
        {
            _loaded = false;
            SetStatus("无法读取记忆，请检查文档格式、大小或文件权限，再点击重新读取。", true);
        }
        UpdateControls();
    }

    private void RefreshList()
    {
        _updating = true;
        var query = SearchBox.Text.Trim();
        var notes = _document.Notes.Where(note => query.Length == 0 ||
            note.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            note.Section.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var view = new ListCollectionView(notes);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MemoryNote.Section), new SectionLabelConverter()));
        NotesList.ItemsSource = view;
        NotesList.SelectedItem = notes.FirstOrDefault(note => note.Id == _selected?.Id);
        EmptyText.Text = _document.Notes.Count == 0
            ? "还没有记忆。点击“添加记忆”，或者聊聊你想让她记住的事。"
            : "没有找到匹配的记忆，试试其他关键词。";
        EmptyText.Visibility = notes.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = query.Length == 0 ? $"共 {_document.Notes.Count} 条" : $"找到 {notes.Length} 条";
        var sectionText = SectionBox.Text;
        SectionBox.ItemsSource = _document.Notes.Select(note => note.Section).Where(section => section.Length > 0)
            .Concat(new[] { "事实", "偏好", "计划" }).Distinct().ToArray();
        SectionBox.Text = sectionText;
        _updating = false;
        UpdateControls();
    }

    private void SelectNote(MemoryNote? note, bool isNew = false)
    {
        _updating = true;
        _selected = note;
        _newNote = isNew;
        _editorDirty = false;
        SectionBox.Text = note?.Section ?? (isNew ? "事实" : "");
        NoteTextBox.Text = note?.Text ?? "";
        EditorPanel.Visibility = note is not null || isNew ? Visibility.Visible : Visibility.Collapsed;
        SelectionHint.Visibility = EditorPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        _updating = false;
        UpdateControls();
    }

    private bool ApplyEditor()
    {
        _removedNoteId = _insertedNoteId = null;
        UpdateEditorDirty();
        if (!_editorDirty) return true;
        var text = NoteTextBox.Text.Trim();
        if (text.Length == 0)
        {
            if (_newNote) { SelectNote(null); return true; }
            SetStatus("记忆内容不能为空；如果不再需要，请点击删除这条。", true);
            return false;
        }
        try
        {
            var section = SectionBox.Text.Trim();
            var originalId = _selected?.Id;
            var moved = _newNote || section != _selected!.Section;
            if (_newNote)
                _document = _document.AddNote(section, text);
            else if (section == _selected!.Section)
                _document = _document.ReplaceNote(_selected.Id, text);
            else
            {
                _removedNoteId = _selected.Id;
                _document = _document.RemoveNote(_selected.Id).AddNote(section, text);
            }
            _editorDirty = false;
            _newNote = false;
            var updated = moved
                ? _document.Notes.LastOrDefault(note => note.Section == section && note.Text == text)
                : _document.Notes.FirstOrDefault(note => note.Id == originalId);
            if (moved) _insertedNoteId = updated?.Id;
            SelectNote(updated);
            return true;
        }
        catch (ArgumentException error)
        {
            SetStatus(error.Message, true);
            return false;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        var next = NotesList.SelectedItem as MemoryNote;
        var nextId = next?.Id;
        if (!ApplyEditor())
        {
            _updating = true;
            NotesList.SelectedItem = _selected;
            _updating = false;
            return;
        }
        if (nextId is not null)
        {
            if (_removedNoteId is not null && nextId > _removedNoteId) nextId--;
            if (_insertedNoteId is not null && nextId >= _insertedNoteId) nextId++;
        }
        SelectNote(nextId is null ? null : _document.Notes.FirstOrDefault(note => note.Id == nextId));
        RefreshList();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !_loaded) return;
        // Keep an active draft visible while filtering; an untouched selection outside the results
        // clears normally so the editor never appears to be a search result when it is not.
        var query = SearchBox.Text.Trim();
        if (!_editorDirty && !_newNote && _selected is not null && query.Length > 0 &&
            !_selected.Text.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !_selected.Section.Contains(query, StringComparison.OrdinalIgnoreCase)) SelectNote(null);
        RefreshList();
    }

    private void OnEditorChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || (!_newNote && _selected is null)) return;
        UpdateEditorDirty();
        UpdateControls();
    }

    private void OnSectionChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        UpdateEditorDirty();
        UpdateControls();
    }

    private void UpdateEditorDirty()
    {
        if (!_newNote && _selected is null) return;
        _editorDirty = _newNote
            ? NoteTextBox.Text.Length > 0 || SectionBox.Text != "事实"
            : NoteTextBox.Text != _selected!.Text || SectionBox.Text != _selected.Section;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (!ApplyEditor()) return;
        SelectNote(null, isNew: true);
        RefreshList();
        NoteTextBox.Focus();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _document = _document.RemoveNote(_selected.Id);
        SelectNote(null);
        RefreshList();
        SetStatus("已移除这条记忆，点击保存修改后生效。撤销修改可恢复。");
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveChanges();

    private bool SaveChanges()
    {
        if (!_loaded || !ApplyEditor()) return false;
        if (_document.Source.Length > ChatMemoryStore.MaxDocumentLength)
        {
            SetStatus($"记忆总长度超过 {ChatMemoryStore.MaxDocumentLength} 个字符，请精简内容后再保存。", true);
            return false;
        }
        try
        {
            _runtime.SaveMemoryDocument(_document.Source, _baseline);
            LoadDocument();
            SetStatus("修改已保存，下一次回复会读取更新后的记忆。之前未整理的对话已丢弃。");
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            SetStatus("保存未完成。文档可能已被后台整理或外部编辑，请先复制当前内容，再重新读取；也请检查文件权限。", true);
            return false;
        }
    }

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return;
        _document = MemoryDocument.Parse(_baseline);
        SelectNote(null);
        RefreshList();
        SetStatus("已撤销本次修改。");
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        if (CanDiscard()) LoadDocument();
    }

    private bool CanDiscard() => !IsDirty || MessageBox.Show(this, "放弃尚未保存的修改？", "记忆管理",
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose || !IsDirty) return;
        var result = MessageBox.Show(this, "记忆有未保存的修改。保存后关闭吗？", "记忆管理",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        e.Cancel = result == MessageBoxResult.Cancel || result == MessageBoxResult.Yes && !SaveChanges();
    }

    private void OnRecordingChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        try
        {
            _runtime.SetMemoryRecordingEnabled(RecordingCheckBox.IsChecked == true);
            UpdateRecordingHelp();
            SetStatus(_runtime.MemoryRecordingEnabled ? "自动记录已开启。" : "自动记录已暂停，已有记忆仍可用于回复。");
        }
        catch (Exception error) when (IsFileError(error) || error is System.Security.Cryptography.CryptographicException)
        {
            _updating = true;
            RecordingCheckBox.IsChecked = _runtime.MemoryRecordingEnabled;
            _updating = false;
            SetStatus("记录设置未保存，请检查文件权限后重试。", true);
        }
    }

    private void UpdateRecordingHelp() => RecordingHelp.Text = _runtime.MemoryRecordingEnabled
        ? "聊天结束后自动整理。取消勾选可暂停新记录与待整理任务，已有记忆仍用于回复。"
        : "已暂停新记录与待整理任务。已有记忆仍用于回复，重新开启后继续整理待办。";

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        if (_closed) return;
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(() => OnRuntimeStateChanged(sender, e)); return; }
        _updating = true;
        RecordingCheckBox.IsChecked = _runtime.MemoryRecordingEnabled;
        _updating = false;
        UpdateRecordingHelp();
    }

    private void UpdateControls()
    {
        if (SaveButton is null) return;
        SaveButton.IsEnabled = IsDirty;
        RevertButton.IsEnabled = IsDirty;
        AddButton.IsEnabled = _loaded;
        DeleteButton.IsEnabled = _loaded && _selected is not null;
        SearchBox.IsEnabled = _loaded;
        NotesList.IsEnabled = EditorPanel.IsEnabled = _loaded;
    }

    private void OnOpenDocument(object sender, RoutedEventArgs e)
    {
        try
        {
            _runtime.EnsureMemoryDocumentExists();
            Process.Start(new ProcessStartInfo(_runtime.MemoryPath) { UseShellExecute = true });
            SetStatus("已打开文档。外部修改后请点击重新读取。");
        }
        catch (Exception error) when (IsFileError(error) || error is Win32Exception)
        {
            SetStatus("无法打开文档，请检查文件权限和 Markdown 的默认打开程序。", true);
        }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        { e.Handled = true; if (SaveButton.IsEnabled) SaveChanges(); }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        { e.Handled = true; SearchBox.Focus(); SearchBox.SelectAll(); }
        else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }

    private static bool IsFileError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException;

    private sealed class SectionLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            string.IsNullOrWhiteSpace(value?.ToString()) ? "未分类" : value;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
