using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AudioPausePlayer;

public partial class SilenceSettingsWindow : Window
{
    public SilenceSettings Settings { get; private set; }

    public SilenceSettingsWindow(SilenceSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        LoadValues(settings);
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new SilenceSettings();
        LoadValues(defaults);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues(out SilenceSettings settings)) return;
        Settings = settings;
        DialogResult = true;
    }

    private void LoadValues(SilenceSettings settings)
    {
        FrameMillisecondsTextBox.Text = settings.FrameMilliseconds.ToString(CultureInfo.InvariantCulture);
        MinimumSilenceTextBox.Text = settings.MinimumSilenceMilliseconds.ToString(CultureInfo.InvariantCulture);
        SilenceThresholdTextBox.Text = settings.SilenceThresholdDb.ToString(CultureInfo.InvariantCulture);
        NavigationToleranceTextBox.Text = settings.NavigationToleranceMilliseconds.ToString(CultureInfo.InvariantCulture);
    }

    private bool TryReadValues(out SilenceSettings settings)
    {
        settings = new SilenceSettings();

        if (!int.TryParse(FrameMillisecondsTextBox.Text.Trim(), out int frameMilliseconds))
            return ShowValueError("分析帧长度必须是整数。");
        if (!int.TryParse(MinimumSilenceTextBox.Text.Trim(), out int minimumSilenceMilliseconds))
            return ShowValueError("最短停顿时长必须是整数。");
        if (!int.TryParse(SilenceThresholdTextBox.Text.Trim(), out int silenceThresholdDb))
            return ShowValueError("静音阈值必须是整数。");
        if (!int.TryParse(NavigationToleranceTextBox.Text.Trim(), out int navigationToleranceMilliseconds))
            return ShowValueError("前后跳转容差必须是整数。");

        var value = new SilenceSettings
        {
            FrameMilliseconds = frameMilliseconds,
            MinimumSilenceMilliseconds = minimumSilenceMilliseconds,
            SilenceThresholdDb = silenceThresholdDb,
            NavigationToleranceMilliseconds = navigationToleranceMilliseconds
        };

        try
        {
            value.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ShowValueError(ex.Message.Split('\n')[0]);
        }

        settings = value;
        return true;
    }

    private void PositiveIntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        string candidate = BuildCandidateText(textBox, e.Text);
        e.Handled = !IsPositiveIntegerText(candidate);
    }

    private void SignedIntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        string candidate = BuildCandidateText(textBox, e.Text);
        e.Handled = !IsSignedIntegerText(candidate);
    }

    private void PositiveIntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        ValidatePaste((TextBox)sender, e, false);
    }

    private void SignedIntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        ValidatePaste((TextBox)sender, e, true);
    }

    private static void ValidatePaste(TextBox textBox, DataObjectPastingEventArgs e, bool allowNegative)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        string pastedText = (string)e.DataObject.GetData(DataFormats.Text);
        string candidate = BuildCandidateText(textBox, pastedText);
        bool valid = allowNegative ? IsSignedIntegerText(candidate) : IsPositiveIntegerText(candidate);
        if (!valid) e.CancelCommand();
    }

    private static string BuildCandidateText(TextBox textBox, string input)
    {
        string current = textBox.Text ?? string.Empty;
        return current.Remove(textBox.SelectionStart, textBox.SelectionLength)
                      .Insert(textBox.SelectionStart, input);
    }

    private static bool IsPositiveIntegerText(string text)
    {
        if (text.Length == 0) return true;

        int start = text[0] == '+' ? 1 : 0;
        if (start == text.Length) return true;

        for (int i = start; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9') return false;
        }
        return true;
    }

    private static bool IsSignedIntegerText(string text)
    {
        if (text.Length == 0) return true;

        int start = text[0] == '+' || text[0] == '-' ? 1 : 0;
        if (start == text.Length) return true;

        for (int i = start; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9') return false;
        }
        return true;
    }

    private bool ShowValueError(string message)
    {
        MessageBox.Show(this, message, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
