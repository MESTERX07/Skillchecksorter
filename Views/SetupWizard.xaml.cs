using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SkillCheckSorter;

public partial class SetupWizard : Window
{
    public AppSettings? Result { get; private set; }

    int _step = 0; // 0 = welcome, 1-3 = data pages

    static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    static readonly Brush DimBrush    = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    static readonly Brush DoneBrush   = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

    public SetupWizard()
    {
        InitializeComponent();

        for (int i = 0; i < 10; i++)
        {
            SourceCombo.Items.Add($"Folder {i}");
            HitCombo.Items.Add($"Folder {i}");
            MissCombo.Items.Add($"Folder {i}");
        }
        SourceCombo.SelectedIndex = 3;
        HitCombo.SelectedIndex    = 1;
        MissCombo.SelectedIndex   = 0;
    }

    void Next_Click(object s, RoutedEventArgs e)
    {
        if (_step == 0) { GoToStep(1); return; }
        if (_step == 3) { TryFinish(); return; }
        GoToStep(_step + 1);
    }

    void Back_Click(object s, RoutedEventArgs e)
    {
        if (_step > 1) GoToStep(_step - 1);
        else GoToStep(0);
    }

    void GoToStep(int step)
    {
        _step = step;

        Page0.Visibility     = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        DataPages.Visibility = step >= 1 ? Visibility.Visible : Visibility.Collapsed;
        Page1.Visibility     = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility     = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility     = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        BackBtn.IsEnabled = step > 0;  // allow going back to welcome from step 1

        NextBtn.Content = step == 3 ? "Finish ✓" : "Next →";

        // Update dot indicators
        Dot1.Fill = step >= 1 ? AccentBrush : DimBrush;
        Dot2.Fill = step >= 2 ? AccentBrush : DimBrush;
        Dot3.Fill = step == 3 ? AccentBrush : DimBrush;

        // Dim completed dots
        if (step > 1) Dot1.Fill = DoneBrush;
        if (step > 2) Dot2.Fill = DoneBrush;

        ConflictWarn.Visibility = Visibility.Collapsed;
    }

    void TryFinish()
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

    void CloseBtn_Click(object s, RoutedEventArgs e) => DialogResult = false;

    void Window_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
