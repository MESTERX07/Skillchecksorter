using System.IO;
using System.Windows;
using System.Windows.Input;

namespace SkillCheckSorter;

public partial class SettingsDialog : Window
{
    public AppSettings? Result { get; private set; }

    public SettingsDialog(AppSettings? current)
    {
        InitializeComponent();

        for (int i = 0; i < 10; i++)
        {
            SourceCombo.Items.Add($"Folder {i}");
            HitCombo.Items.Add($"Folder {i}");
            MissCombo.Items.Add($"Folder {i}");
        }

        if (current is { } s)
        {
            SourceCombo.SelectedIndex = s.SourceFolder;
            HitCombo.SelectedIndex    = s.HitFolder;
            MissCombo.SelectedIndex   = s.MissFolder;
        }
        else
        {
            SourceCombo.SelectedIndex = 3;
            HitCombo.SelectedIndex    = 1;
            MissCombo.SelectedIndex   = 0;
        }
    }

    void SaveBtn_Click(object s, RoutedEventArgs e)
    {
        int src  = SourceCombo.SelectedIndex;
        int hit  = HitCombo.SelectedIndex;
        int miss = MissCombo.SelectedIndex;

        if (hit == miss)
        {
            ConflictWarn.Visibility = Visibility.Visible;
            return;
        }
        Result = new AppSettings(src, hit, miss);
        DialogResult = true;
    }

    void CancelBtn_Click(object s, RoutedEventArgs e) => DialogResult = false;
    void CloseBtn_Click(object s,  RoutedEventArgs e) => DialogResult = false;

    void UninstallBtn_Click(object s, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "This will uninstall SkillCheckSorter and remove all its files and settings.\n\n" +
            "Your sorted image folders will not be affected.",
            "Uninstall SkillCheckSorter?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes) return;

        string appDir = Path.GetDirectoryName(
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? AppContext.BaseDirectory)!;

        string uninstaller = Path.Combine(appDir, "Uninstall.exe");

        if (File.Exists(uninstaller))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uninstaller)
            {
                UseShellExecute = true  // required for UAC elevation prompt
            });
        }
        else
        {
            // Running outside the installer — just wipe settings.json
            try { File.Delete(Path.Combine(appDir, "settings.json")); } catch { }
        }

        System.Windows.Application.Current.Shutdown();
    }

    void Window_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
