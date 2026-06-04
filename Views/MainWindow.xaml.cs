using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SkillCheckSorter;

record UndoEntry(int ImageIndex, string OriginalPath, string DestPath, int OriginalFolder, bool IsDelete = false, bool IsUnsure = false);
public partial class MainWindow : Window
{
    // ── Paths ────────────────────────────────────────────────────────────────
    static readonly string AppDir = Path.GetDirectoryName(
        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? AppContext.BaseDirectory)!;
    static readonly string SettingsPath = Path.Combine(AppDir, "settings.json");
    static readonly HashSet<string> ImageExts = [".png", ".jpg", ".jpeg", ".bmp"];

    // ── State ────────────────────────────────────────────────────────────────
    AppSettings? _settings;
    DirectoryInfo? _sessionDir;
    List<(FileInfo File, int Folder)> _images = [];
    int _idx;
    readonly Stack<UndoEntry> _undo = new();
    readonly Dictionary<string, BitmapImage> _cache = new();
    readonly Queue<string> _cacheOrder = new();     // tracks insertion order for eviction
    bool _suppressCombos;
    int _deleted;
    int _unsure;
    int _hitBaseline;
    int _missBaseline;

    // Hover colours: normalBg, hoverBg, normalBorder, hoverBorder
    record HoverState(Brush NBg, Brush HBg, Brush NBrd, Brush HBrd);
    readonly Dictionary<Button, HoverState> _hovers = [];

    public MainWindow()
    {
        InitializeComponent();
        _settings = LoadSettings();

        InitConfigCombos();
        RegisterHovers();
    }

    // ── Startup ──────────────────────────────────────────────────────────────

    void Window_Loaded(object s, RoutedEventArgs e)
    {
        if (_settings is null)
            RunWizard();
    }

    void RunWizard()
    {
        var wizard = new SetupWizard { Owner = this };
        if (wizard.ShowDialog() == true && wizard.Result is { } result)
        {
            SaveSettings(result);
            ApplySettings();
        }
    }

    // ── Settings persistence ──────────────────────────────────────────────────

    AppSettings? LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var raw = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (raw is { } r)
            {
                var s = AppSettings.Validated(r.SourceFolder, r.HitFolder, r.MissFolder);
                if (s.HitFolder != s.MissFolder) return s;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadSettings: {ex.Message}"); }
        return null;
    }

    void SaveSettings(AppSettings s)
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s));
        _settings = s;
    }

    // ── Config bar ────────────────────────────────────────────────────────────

    void InitConfigCombos()
    {
        _suppressCombos = true;
        for (int i = 0; i < 10; i++)
        {
            SourceCombo.Items.Add($"Folder {i}");
            HitCombo.Items.Add($"Folder {i}");
            MissCombo.Items.Add($"Folder {i}");
        }
        _suppressCombos = false;
        ApplySettings();
    }

    void ApplySettings()
    {
        _suppressCombos = true;
        if (_settings is { } s)
        {
            SourceCombo.SelectedIndex = s.SourceFolder;
            HitCombo.SelectedIndex    = s.HitFolder;
            MissCombo.SelectedIndex   = s.MissFolder;
            CfgBar.Visibility    = Visibility.Visible;
            CfgPrompt.Visibility = Visibility.Collapsed;
            OpenBtn.IsEnabled    = true;
        }
        else
        {
            CfgBar.Visibility    = Visibility.Collapsed;
            CfgPrompt.Visibility = Visibility.Visible;
            OpenBtn.IsEnabled    = false;
        }
        _suppressCombos = false;
        SetActionsEnabled(false);
    }

    void SourceCombo_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_suppressCombos) return;
        bool saved = CommitComboChanges();
        // Only reload if the new settings were actually saved (no HIT/MISS conflict)
        if (saved && _sessionDir is not null)
            LoadSession(_sessionDir);
    }

    void HitCombo_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_suppressCombos) return;
        CommitComboChanges();
    }

    void MissCombo_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_suppressCombos) return;
        CommitComboChanges();
    }

    // Returns true if settings were saved (no conflict)
    bool CommitComboChanges()
    {
        int src  = SourceCombo.SelectedIndex;
        int hit  = HitCombo.SelectedIndex;
        int miss = MissCombo.SelectedIndex;

        bool conflict = hit == miss;
        CfgConflict.Visibility = conflict ? Visibility.Visible : Visibility.Collapsed;

        if (!conflict)
        {
            SaveSettings(new AppSettings(src, hit, miss));
            return true;
        }
        return false;
    }

    // ── Gear / caption handlers ───────────────────────────────────────────────

    void IndexBtn_Click(object s, RoutedEventArgs e) => IndexPopup.IsOpen = !IndexPopup.IsOpen;

    void GearBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_settings) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is { } result)
        {
            SaveSettings(result);
            ApplySettings();
            if (_sessionDir is not null) LoadSession(_sessionDir);
        }
    }

    void MinBtn_Click(object s,   RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void CloseBtn_Click(object s, RoutedEventArgs e) => Close();
    void Window_Closing(object s, System.ComponentModel.CancelEventArgs e) => CleanTrash();
    void MaxBtn_Click(object s,   RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Window_StateChanged(object? s, EventArgs e) =>
        MaxBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";

    void TopBar_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    // ── Drag & drop ───────────────────────────────────────────────────────────

    void Window_DragOver(object s, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object s, DragEventArgs e)
    {
        if (_settings is null) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (paths.Length == 1 && Directory.Exists(paths[0]))
            LoadSession(new DirectoryInfo(paths[0]));
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    void Window_KeyDown(object s, KeyEventArgs e)
    {
        if (_settings is null) return;
        // Don't hijack keys when a ComboBox or TextBox has keyboard focus
        if (Keyboard.FocusedElement is ComboBox or TextBox) return;
        switch (e.Key)
        {
            case Key.H or Key.Right:  Sort("hit");    break;
            case Key.M or Key.Left:   Sort("miss");   break;
            case Key.U:               SortUnsure();   break;
            case Key.Delete:          DeleteImage();  break;
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control: UndoAction(); break;
        }
    }

    // ── Open folder ───────────────────────────────────────────────────────────

    void OpenBtn_Click(object s, RoutedEventArgs e)
    {
        if (_settings is null) return;
        // FolderBrowserDialog is reliable on all Windows versions
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description        = "Select a session folder containing image subfolders (0-9)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK
            && !string.IsNullOrEmpty(dlg.SelectedPath))
            LoadSession(new DirectoryInfo(dlg.SelectedPath));
    }

    // ── Session loading ───────────────────────────────────────────────────────

    void LoadSession(DirectoryInfo dir)
    {
        if (_settings is null) return;

        CleanTrash(); // wipe previous session's trash before switching
        _sessionDir = dir;
        var disp = dir.FullName.Length > 80 ? "…" + dir.FullName[^77..] : dir.FullName;
        FolderLabel.Text = disp;

        _images.Clear();
        _undo.Clear();
        _cache.Clear();
        _cacheOrder.Clear();
        _deleted      = 0;
        _unsure       = 0;
        _hitBaseline  = _settings is { } s0 ? CountFolder(s0.HitFolder)  : 0;
        _missBaseline = _settings is { } s1 ? CountFolder(s1.MissFolder) : 0;

        var sub = new DirectoryInfo(
            Path.Combine(dir.FullName, _settings.SourceFolder.ToString()));

        if (sub.Exists)
        {
            foreach (var f in sub.EnumerateFiles().OrderBy(f => f.Name))
                if (ImageExts.Contains(f.Extension.ToLowerInvariant()))
                    _images.Add((f, _settings.SourceFolder));
        }

        _idx = 0;
        PlaceholderText.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

        if (_images.Count == 0)
        {
            SetImage(null);
            PlaceholderText.Text =
                $"No images found in folder {_settings.SourceFolder}\n\n" +
                $"Make sure the session folder contains a subfolder named \"{_settings.SourceFolder}\"";
            ClearInfoBar();
            RefreshStats();
            SetActionsEnabled(false);
            return;
        }

        SetActionsEnabled(true);
        ShowCurrent();
    }

    // ── Image display ─────────────────────────────────────────────────────────

    void ShowCurrent()
    {
        if (_idx >= _images.Count) { ShowDone(); return; }

        var (file, folder) = _images[_idx];
        var key = file.FullName;

        if (_cache.TryGetValue(key, out var cached))
            SetImage(cached);
        else
        {
            var bmp = LoadBitmap(file.FullName);
            if (bmp is not null) { StoreCache(key, bmp); }
            SetImage(bmp);
        }

        NameLabel.Text       = file.Name;
        FolderNumLabel.Text  = $"folder {folder}";
        CounterLabel.Text    = $"{_idx + 1} / {_images.Count}";

        if (_idx + 1 < _images.Count)
            PreloadNext(_images[_idx + 1].File.FullName);

        RefreshStats();
    }

    void SetImage(BitmapImage? bmp)
    {
        MainImage.Source           = bmp;
        PlaceholderText.Visibility = bmp is null ? Visibility.Visible : Visibility.Collapsed;
    }

    static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource   = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadBitmap {path}: {ex.Message}"); return null; }
    }

    // Preload on the UI thread at idle/background priority - avoids BitmapImage MTA threading issues
    void PreloadNext(string path)
    {
        if (_cache.ContainsKey(path)) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (_cache.ContainsKey(path)) return;
            var bmp = LoadBitmap(path);
            if (bmp is not null) StoreCache(path, bmp);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void StoreCache(string key, BitmapImage bmp)
    {
        if (!_cache.ContainsKey(key))
            _cacheOrder.Enqueue(key);
        _cache[key] = bmp;
        // Evict oldest entry once we exceed capacity
        while (_cacheOrder.Count > 6)
        {
            var oldest = _cacheOrder.Dequeue();
            _cache.Remove(oldest);
        }
    }

    // ── Sort ─────────────────────────────────────────────────────────────────

    void Sort(string action)
    {
        if (_settings is null) return;
        int target = action == "hit" ? _settings.HitFolder : _settings.MissFolder;
        MoveToFolder(target);
    }

    void SortUnsure() => MoveToFolder(0, isUnsure: true);

    void MoveToFolder(int target, bool isUnsure = false)
    {
        if (_settings is null || _images.Count == 0 || _idx >= _images.Count) return;

        var (file, folder) = _images[_idx];

        // Image is already in the destination - just advance
        if (folder == target) { _idx++; ShowCurrent(); return; }

        var destDir = new DirectoryInfo(
            Path.Combine(_sessionDir!.FullName, target.ToString()));
        destDir.Create();
        var dest = UniquePath(destDir, file);

        try   { File.Move(file.FullName, dest); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not move file:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (isUnsure) _unsure++;
        _undo.Push(new UndoEntry(_idx, file.FullName, dest, folder, IsUnsure: isUnsure));
        _cache.Remove(file.FullName);
        _images[_idx] = (new FileInfo(dest), target);

        _idx++;
        ShowCurrent();
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    void UndoAction()
    {
        if (_undo.Count == 0 || _sessionDir is null) return;
        var entry = _undo.Pop();

        var restoreDir = new DirectoryInfo(
            Path.Combine(_sessionDir.FullName, entry.OriginalFolder.ToString()));
        restoreDir.Create();
        var restore = UniquePath(restoreDir, new FileInfo(entry.OriginalPath));

        try   { File.Move(entry.DestPath, restore); }
        catch (Exception ex)
        {
            _undo.Push(entry); // put it back — nothing changed
            MessageBox.Show($"Could not undo:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _cache.Remove(entry.DestPath);

        if (entry.IsDelete)
        {
            // Re-insert the recovered file at its original position
            int insertAt = Math.Min(entry.ImageIndex, _images.Count);
            _images.Insert(insertAt, (new FileInfo(restore), entry.OriginalFolder));
            _idx = insertAt;
            _deleted--;
        }
        else
        {
            _images[entry.ImageIndex] = (new FileInfo(restore), entry.OriginalFolder);
            _idx = entry.ImageIndex;
            if (entry.IsUnsure) _unsure--;
        }

        SetActionsEnabled(true);
        ShowCurrent();
    }

    // ── Delete (moves to .trash, undoable) ───────────────────────────────────

    void DeleteImage()
    {
        if (_images.Count == 0 || _idx >= _images.Count) return;
        var (file, folder) = _images[_idx];

        var trashDir = new DirectoryInfo(Path.Combine(_sessionDir!.FullName, ".trash"));
        trashDir.Create();
        var trashPath = UniquePath(trashDir, file);

        try   { File.Move(file.FullName, trashPath); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _undo.Push(new UndoEntry(_idx, file.FullName, trashPath, folder, IsDelete: true));
        _cache.Remove(file.FullName);
        _images.RemoveAt(_idx);
        if (_idx >= _images.Count) _idx = Math.Max(0, _images.Count - 1);
        _deleted++;

        ShowCurrent();
    }

    // ── Button click forwarding ───────────────────────────────────────────────

    void HitBtn_Click(object s,    RoutedEventArgs e) => Sort("hit");
    void MissBtn_Click(object s,   RoutedEventArgs e) => Sort("miss");
    void UnsureBtn_Click(object s, RoutedEventArgs e) => SortUnsure();
    void UndoBtn_Click(object s,   RoutedEventArgs e) => UndoAction();
    void DelBtn_Click(object s,    RoutedEventArgs e) => DeleteImage();

    // ── Stats ─────────────────────────────────────────────────────────────────

    int CountFolder(int n)
    {
        if (_sessionDir is null) return 0;
        var d = new DirectoryInfo(Path.Combine(_sessionDir.FullName, n.ToString()));
        return d.Exists
            ? d.EnumerateFiles().Count(f => ImageExts.Contains(f.Extension.ToLowerInvariant()))
            : 0;
    }

    void RefreshStats()
    {
        if (_settings is null) return;
        int remaining = Math.Max(0, _images.Count - _idx);
        int hitN      = CountFolder(_settings.HitFolder)  - _hitBaseline;
        int missN     = CountFolder(_settings.MissFolder) - _missBaseline;

        RemLabel.Text    = $"Left to sort: {remaining}";
        HitLabel.Text    = $"HIT: {hitN}";
        MissLabel.Text   = $"MISS: {missN}";
        UnsureLabel.Text = $"UNSURE: {_unsure}";
        DelLabel.Text    = $"DEL: {_deleted}";

        ProgressBar.Value = _images.Count > 0
            ? (double)_idx / _images.Count * 1000 : 0;
        UndoBtn.IsEnabled = _undo.Count > 0;
        UndoBtn.Content   = _undo.Count > 0 ? $"↩  Undo  {_undo.Count}" : "↩  Undo";
    }

    // ── Done screen ───────────────────────────────────────────────────────────

    void ShowDone()
    {
        if (_settings is null) return;

        CleanTrash();
        PurgeDeleteUndos();

        int hitN  = CountFolder(_settings.HitFolder)  - _hitBaseline;
        int missN = CountFolder(_settings.MissFolder) - _missBaseline;

        SetImage(null);
        PlaceholderText.Text = $"Session complete\n\nHIT: {hitN}     MISS: {missN}     UNSURE: {_unsure}     DEL: {_deleted}";
        PlaceholderText.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

        ClearInfoBar("Session complete");
        HitBtn.IsEnabled    = false;
        MissBtn.IsEnabled   = false;
        UnsureBtn.IsEnabled = false;
        DelBtn.IsEnabled    = false;
        UndoBtn.IsEnabled = _undo.Count > 0;
        UndoBtn.Content   = _undo.Count > 0 ? $"↩  Undo  {_undo.Count}" : "↩  Undo";

        _deleted = 0;
        _unsure  = 0;
        ProgressBar.Value = 0;
        RemLabel.Text    = "";
        HitLabel.Text    = "HIT: 0";
        MissLabel.Text   = "MISS: 0";
        UnsureLabel.Text = "UNSURE: 0";
        DelLabel.Text    = "DEL: 0";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void CleanTrash()
    {
        if (_sessionDir is null) return;
        var trash = new DirectoryInfo(Path.Combine(_sessionDir.FullName, ".trash"));
        if (!trash.Exists) return;
        try
        {
            trash.Delete(recursive: true);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CleanTrash: {ex.Message}"); }
    }

    void PurgeDeleteUndos()
    {
        var keep = _undo.Where(e => !e.IsDelete).ToArray(); // top→bottom order
        _undo.Clear();
        foreach (var e in keep.Reverse()) // push bottom→top so stack order is preserved
            _undo.Push(e);
    }

    void SetActionsEnabled(bool on)
    {
        HitBtn.IsEnabled    = on;
        MissBtn.IsEnabled   = on;
        UnsureBtn.IsEnabled = on;
        DelBtn.IsEnabled    = on;
        UndoBtn.IsEnabled   = on && _undo.Count > 0;
    }

    void ClearInfoBar(string? status = null)
    {
        NameLabel.Text       = status ?? "";
        FolderNumLabel.Text  = "";
        CounterLabel.Text    = "";
    }

    static string UniquePath(DirectoryInfo dir, FileInfo file)
    {
        var dest = Path.Combine(dir.FullName, file.Name);
        if (!File.Exists(dest)) return dest;
        int n = 1;
        string stem = Path.GetFileNameWithoutExtension(file.Name);
        string ext  = file.Extension;
        while (File.Exists(dest))
            dest = Path.Combine(dir.FullName, $"{stem}_{n++}{ext}");
        return dest;
    }

    // ── Hover wiring ─────────────────────────────────────────────────────────

    void RegisterHovers()
    {
        Loaded += (_, _) =>
        {
            Hook(HitBtn,    "#0C1A0C", "#14281A", "#1E3A1E", "#3A6A3A");
            Hook(MissBtn,   "#1A0C0C", "#281414", "#3A1E1E", "#6A3A3A");
            Hook(UnsureBtn, "#181400", "#221C00", "#362C00", "#5A4800");
            Hook(UndoBtn,   "#141414", "#1C1C1C", "#252525", "#404040");
            Hook(DelBtn,    "#0F0F0F", "#1A1010", "#1C1C1C", "#3A2020");
            Hook(OpenBtn,   "#141414", "#1E1E1E", "#2A2A2A", "#555555");
            Hook(IndexBtn,  "Transparent", "#1A1A1A", "Transparent", "Transparent");
            Hook(GearBtn,   "Transparent", "#1A1A1A", "Transparent", "Transparent");
            Hook(MinBtn,    "Transparent", "#1A1A1A", "Transparent", "Transparent");
            Hook(MaxBtn,    "Transparent", "#1A1A1A", "Transparent", "Transparent");
            Hook(CloseBtn,  "Transparent", "#3A1010", "Transparent", "Transparent");
        };
    }

    void Hook(Button btn, string normal, string hover, string normalBd, string hoverBd)
    {
        var h = new HoverState(Br(normal), Br(hover), Br(normalBd), Br(hoverBd));
        _hovers[btn] = h;
        btn.MouseEnter += (_, _) => { if (btn.IsEnabled) { btn.Background = h.HBg; btn.BorderBrush = h.HBrd; } };
        btn.MouseLeave += (_, _) => { btn.Background = h.NBg; btn.BorderBrush = h.NBrd; };
    }

    static SolidColorBrush Br(string hex) =>
        hex is "Transparent"
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
