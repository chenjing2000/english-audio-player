using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AudioPausePlayer;

public partial class MainWindow : Window
{
    private readonly AudioPlayer player = new();
    private readonly ObservableCollection<AudioTrack> tracks = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer notificationTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private SilenceSettings silenceSettings = new();
    private List<SilenceRange> ranges = new();
    private AudioTrack? loadedTrack;
    private bool operationBusy;
    private bool analyzing;
    private bool analysisReady;
    private bool dragging;
    private bool closing;
    private bool allowClose;
    private Task? activeOperation;
    private Task? analysisTask;
    private readonly Queue<Key> navigationKeys = new();
    private bool navigating;
    private Task navigationTask = Task.CompletedTask;
    private Exception? displayedPlaybackError;

    public MainWindow()
    {
        InitializeComponent();
        Rect workArea = SystemParameters.WorkArea;
        double desiredWidth = Math.Round(workArea.Width * 0.5);
        double desiredHeight = Math.Round(workArea.Height * 0.6);
        Width = Math.Clamp(desiredWidth, MinWidth, MaxWidth);
        Height = Math.Clamp(desiredHeight, MinHeight, MaxHeight);
        Left = Math.Round(workArea.Left + (workArea.Width - Width) / 2);
        Top = Math.Round(workArea.Top + (workArea.Height - Height) / 2);
        SourceInitialized += MainWindow_SourceInitialized;
        ProgressSlider.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ProgressSlider_MouseDown), true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(ProgressSlider_MouseUp), true);
        TrackList.ItemsSource = tracks;
        player.StateChanged += Player_StateChanged;
        timer.Tick += (_, _) => RefreshProgress();
        timer.Start();
        notificationTimer.Tick += (_, _) => HideNotification();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        WindowStyleHelper.DisableMaximizeButton(this);
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (analyzing || operationBusy || closing) return;
        var dialog = new OpenFolderDialog { Title = "选择教材音频文件夹" };
        if (dialog.ShowDialog(this) == true) await RunActionAsync(() => LoadFolderAsync(dialog.FolderName));
    }

    internal async Task LoadFolderAsync(string path)
    {
        // Keep the current folder and player if scanning the replacement folder fails.
        var files = await Task.Run(() => AudioLibrary.LoadFolder(path));
        if (closing) return;
        await player.CloseAsync();
        loadedTrack = null;
        ranges.Clear();
        analysisReady = false;
        tracks.Clear();
        foreach (var file in files) tracks.Add(file);
        int errors = files.Count(file => file.LoadError is not null);
        if (errors > 0) ShowNotification($"{errors} 个文件的标签读取失败，悬停对应行可查看原因。", false);
        else if (files.Count == 0) ShowNotification("此文件夹中没有 MP3 文件。", false);
        RefreshProgress();
        UpdateControls();
    }

    private async void TrackList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (analyzing || operationBusy || closing || e.ChangedButton != MouseButton.Left) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not DataGridRow)
            source = VisualTreeHelper.GetParent(source);
        if (source is DataGridRow row && row.Item is AudioTrack track)
            await RunActionAsync(() => PlayTrackAsync(track));
    }

    private async void TrackRow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (sender is not DataGridRow row || !row.IsKeyboardFocusWithin) return;

        e.Handled = true;
        if (analyzing || operationBusy || closing || row.Item is not AudioTrack track) return;
        await RunActionAsync(() => PlayTrackAsync(track));
    }

    private async Task PlayTrackAsync(AudioTrack track)
    {
        if (!ReferenceEquals(track, loadedTrack))
        {
            await LoadTrackAsync(track);
            return;
        }

        if (!player.IsPlaying)
            await player.PlayAsync();
    }

    private async Task LoadTrackAsync(AudioTrack track)
    {
        ranges.Clear();
        analysisReady = false;
        await player.OpenAsync(track.FilePath);
        loadedTrack = track;
        if (closing) return;
        await player.PlayAsync();
        analyzing = true;
        analysisTask = AnalyzeAsync(track, silenceSettings);
        UpdateControls();
    }

    private async Task AnalyzeAsync(AudioTrack track, SilenceSettings settings)
    {
        try
        {
            var result = await Task.Run(() => SilenceDetector.Detect(track.FilePath, settings));
            if (closing || !ReferenceEquals(track, loadedTrack)) return;
            ranges = result;
            analysisReady = true;
        }
        catch (Exception ex)
        {
            if (!closing) ShowError("停顿分析失败，普通播放仍可使用：" + ex.Message);
        }
        finally
        {
            analyzing = false;
            if (!closing) UpdateControls();
        }
    }

    private async Task TogglePlaybackAsync()
    {
        if (player.IsLoaded)
        {
            if (player.IsPlaying) player.Pause();
            else await player.PlayAsync();
        }
        else
        {
            var track = TrackList.SelectedItem as AudioTrack ?? tracks.FirstOrDefault();
            if (track is not null && !analyzing) await LoadTrackAsync(track);
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await RunActionAsync(TogglePlaybackAsync);
    private async void StopButton_Click(object sender, RoutedEventArgs e) => await RunActionAsync(player.StopAsync);

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (analyzing || operationBusy || closing) return;

        var dialog = new SilenceSettingsWindow(silenceSettings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        silenceSettings = dialog.Settings;
        if (loadedTrack is null) return;

        ranges.Clear();
        analysisReady = false;
        analyzing = true;
        analysisTask = AnalyzeAsync(loadedTrack, silenceSettings);
        UpdateControls();
        await analysisTask;
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || !IsPlaybackKey(e.Key)) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        await HandleShortcutAsync(e.Key);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && IsPlaybackKey(e.Key)) e.Handled = true;
    }

    private static bool IsPlaybackKey(Key key) => key == Key.Space || key == Key.Left || key == Key.Right;

    internal Task HandleShortcutAsync(Key key)
    {
        if (key == Key.Space) return RunActionAsync(TogglePlaybackAsync);
        if (key != Key.Left && key != Key.Right) return Task.CompletedTask;
        if (closing || dragging || analyzing || !analysisReady || (operationBusy && !navigating)) return Task.CompletedTask;
        navigationKeys.Enqueue(key);
        if (!navigating)
        {
            navigating = true;
            navigationTask = DrainNavigationAsync();
        }
        return navigationTask;
    }

    private async Task DrainNavigationAsync()
    {
        try
        {
            while (navigationKeys.Count > 0 && !closing)
            {
                Key key = navigationKeys.Dequeue();
                await RunActionAsync(async () =>
                {
                    TimeSpan current = player.GetCurrentTime();
                    TimeSpan? target = key == Key.Left
                        ? SilenceDetector.FindPrevious(ranges, current, silenceSettings)
                        : SilenceDetector.FindNext(ranges, current, silenceSettings);
                    if (target.HasValue) await player.SeekAsync(target.Value);
                });
            }
        }
        finally { navigationKeys.Clear(); navigating = false; }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (operationBusy || closing) return;
        operationBusy = true;
        UpdateControls();
        Task work = RunCoreAsync(action);
        activeOperation = work;
        try { await work; }
        finally
        {
            activeOperation = null;
            operationBusy = false;
            if (!closing) { UpdateControls(); RefreshProgress(); }
        }
    }

    private async Task RunCoreAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            if (!closing)
            {
                ShowError(ex.Message);
            }
        }
    }

    private void Player_StateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Player_StateChanged(sender, e)); return; }
        if (closing) return;
        UpdateControls();
        RefreshProgress();
        if (player.LastError is null) displayedPlaybackError = null;
        else if (!ReferenceEquals(player.LastError, displayedPlaybackError))
        {
            displayedPlaybackError = player.LastError;
            ShowError("播放设备异常：" + player.LastError.Message);
        }
    }

    private void UpdateControls()
    {
        bool available = !operationBusy && !closing;
        OpenButton.IsEnabled = available && !analyzing;
        SettingsButton.IsEnabled = available && !analyzing;
        TrackList.IsEnabled = available && !analyzing;
        PlayButton.IsEnabled = available && (player.IsLoaded || tracks.Count > 0);
        StopButton.IsEnabled = available && player.IsLoaded;
        ProgressSlider.IsEnabled = available && player.IsLoaded;
        bool playing = player.IsPlaying;
        PlayIconImage.Source = (ImageSource)FindResource(playing ? "PauseIcon" : "PlayIcon");
        PlayButton.ToolTip = playing ? "暂停" : "播放";
        AutomationProperties.SetName(PlayButton, playing ? "暂停" : "播放");
        foreach (var track in tracks)
        {
            bool loaded = ReferenceEquals(track, loadedTrack);
            track.IsPlaying = loaded && player.IsPlaying;
            track.IsPaused = loaded && player.IsPaused;
        }
    }

    private void RefreshProgress()
    {
        if (dragging || closing) return;
        double duration = player.GetDuration().TotalSeconds;
        ProgressSlider.Maximum = Math.Max(duration, 0.001);
        ProgressSlider.Value = Math.Min(player.GetCurrentTime().TotalSeconds, duration);
        UpdateTimeText();
    }

    private void ShowError(string message) => ShowNotification(message, true);

    private void PlaylistBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PlaylistBorder.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
    }

    internal void ShowNotification(string message, bool isError)
    {
        if (closing) return;
        notificationTimer.Stop();
        NotificationText.Text = (isError ? "错误：" : "警告：") + message;
        NotificationBar.Background = new SolidColorBrush(isError ? Color.FromRgb(253, 232, 232) : Color.FromRgb(255, 244, 214));
        NotificationText.Foreground = new SolidColorBrush(isError ? Color.FromRgb(153, 27, 27) : Color.FromRgb(146, 64, 14));
        NotificationBar.Visibility = Visibility.Visible;
        notificationTimer.Start();
    }

    private void HideNotification()
    {
        notificationTimer.Stop();
        NotificationText.Text = "";
        NotificationBar.Visibility = Visibility.Collapsed;
    }

    private void UpdateTimeText() => TimeText.Text = AudioTrack.FormatTime(TimeSpan.FromSeconds(ProgressSlider.Value)) + " / " + AudioTrack.FormatTime(player.GetDuration());

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized) UpdateTimeText();
    }

    private void ProgressSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (operationBusy || closing || !player.IsLoaded) return;
        dragging = true;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, ProgressSlider))
        {
            if (source is Thumb) return; // Thumb manages its own drag capture.
            source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
        }
        ProgressSlider.CaptureMouse();
    }

    private async void ProgressSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) await FinishDragAsync();
    }
    private async void ProgressSlider_LostMouseCapture(object sender, MouseEventArgs e) => await FinishDragAsync();

    private async Task FinishDragAsync()
    {
        if (!dragging) return;
        TimeSpan target = TimeSpan.FromSeconds(ProgressSlider.Value);
        dragging = false;
        if (ReferenceEquals(Mouse.Captured, ProgressSlider)) ProgressSlider.ReleaseMouseCapture();
        await RunActionAsync(() => player.SeekAsync(target));
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        timer.Stop();
        HideNotification();
        UpdateControls();
        try
        {
            if (activeOperation is not null) await activeOperation;
            await navigationTask;
            if (analysisTask is not null) await analysisTask;
            await player.CloseAsync();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        finally
        {
            player.StateChanged -= Player_StateChanged;
            player.Dispose();
            allowClose = true;
            Close();
        }
    }
}
