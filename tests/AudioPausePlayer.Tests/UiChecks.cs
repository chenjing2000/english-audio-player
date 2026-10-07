using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioPausePlayer;
using NAudio.Wave;

internal static class UiChecks
{
    public static Task<int> RunAsync(string audioFolder)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var appXml = System.Xml.Linq.XDocument.Load(Path.GetFullPath("EnglishAudioPlayer/App.xaml"));
            var dictionary = appXml.Root!.Elements().Single().Elements().Single();
            dictionary.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            string resourceXml = dictionary.ToString();
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resourceXml);
            app.Startup += async (_, _) =>
            {
                MainWindow? window = null;
                int failed = 0, passed = 0;
                void Assert(bool value, string message)
                {
                    if (!value) throw new Exception(message);
                    passed++;
                    Console.WriteLine("PASS UI " + message);
                }
                try
                {
                    window = new MainWindow();
                    Rect workArea = SystemParameters.WorkArea;
                    Assert(Math.Abs(window.Left - (workArea.Left + (workArea.Width - window.Width) / 2)) < 0.01 && Math.Abs(window.Top - (workArea.Top + (workArea.Height - window.Height) / 2)) < 0.01, "Window starts centered in available screen");
                    window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -20000; window.Top = -20000;
                    window.Show();
                    // The native window rounds its dimensions to whole physical pixels.
                    Assert(Math.Abs(window.Width - SystemParameters.PrimaryScreenWidth * 0.5) < 1 && Math.Abs(window.Height - SystemParameters.WorkArea.Height * 0.6) < 1, "Default size uses screen proportions");
                    Assert(Math.Abs(window.MaxWidth - SystemParameters.PrimaryScreenWidth * 0.7) < 0.01 && Math.Abs(window.MaxHeight - SystemParameters.WorkArea.Height * 0.8) < 0.01, "Maximum size uses screen proportions");
                    Assert(Math.Abs(window.MinWidth - 520) < 0.01 && Math.Abs(window.MinHeight - 400) < 0.01, "Minimum window size is 520x400");
                    var notification = (Border)window.FindName("NotificationBar");
                    var notificationText = (TextBlock)window.FindName("NotificationText");
                    Assert(notification.Visibility == Visibility.Collapsed, "Notification normally collapsed");
                    window.ShowNotification("测试警告", false);
                    Assert(notification.Visibility == Visibility.Visible && notificationText.Text == "警告：测试警告", "Warning appears inline");
                    await Task.Delay(2000);
                    window.ShowNotification("测试错误", true);
                    Assert(notificationText.Text == "错误：测试错误", "Error replaces warning inline");
                    await Task.Delay(3000);
                    Assert(notification.Visibility == Visibility.Visible, "New message resets five-second timer");
                    await Task.Delay(2200);
                    Assert(notification.Visibility == Visibility.Collapsed && notificationText.Text == "", "Message clears and bar collapses after five seconds");
                    await window.LoadFolderAsync(audioFolder);
                    var list = (DataGrid)window.FindName("TrackList");
                    var slider = (Slider)window.FindName("ProgressSlider");
                    var timeText = (TextBlock)window.FindName("TimeText");
                    var openButton = (Button)window.FindName("OpenButton");
                    var playButton = (Button)window.FindName("PlayButton");
                    var stopButton = (Button)window.FindName("StopButton");
                    var settingsButton = (Button)window.FindName("SettingsButton");
                    settingsButton.ApplyTemplate();
                    var settingsBorder = (Border)settingsButton.Template.FindName("ButtonBorder", settingsButton);
                    var settingsImage = FindVisual<Image>(settingsButton)!;
                    Assert(Math.Abs(settingsButton.Width - 36) < 0.01 && Math.Abs(settingsButton.Height - 36) < 0.01 &&
                           Math.Abs(settingsBorder.CornerRadius.TopLeft - 18) < 0.01, "Settings button is circular");
                    Assert(Math.Abs(settingsImage.Width - 22) < 0.01 && Math.Abs(settingsImage.Height - 22) < 0.01, "Settings icon is 22x22");
                    Assert(Grid.GetColumn(stopButton) == 4 && Grid.GetColumn(settingsButton) == 5, "Settings button is immediately to the right of stop button");
                    Assert(settingsButton.HorizontalAlignment == HorizontalAlignment.Center && settingsButton.VerticalAlignment == VerticalAlignment.Center, "Settings button is centered in the top row");
                    Assert(settingsButton.FocusVisualStyle is null, "Settings button has no focus visual");
                    var settingsWindow = new SilenceSettingsWindow(new SilenceSettings());
                    Assert(Math.Abs(settingsWindow.Width - 324) < 0.01, "Settings window width is 324");
                    Assert(Math.Abs(settingsWindow.Height - 350) < 0.01, "Settings window height is 350");
                    foreach (string textBoxName in new[] { "FrameMillisecondsTextBox", "MinimumSilenceTextBox", "SilenceThresholdTextBox", "NavigationToleranceTextBox" })
                    {
                        var textBox = (TextBox)settingsWindow.FindName(textBoxName);
                        Assert(Math.Abs(textBox.Width - 80) < 0.01 && textBox.TextAlignment == TextAlignment.Center && textBox.VerticalContentAlignment == VerticalAlignment.Center && !textBox.AllowDrop, textBoxName + " is 80 wide, centers its value, and blocks drag/drop text");
                    }
                    MethodInfo positiveInput = typeof(SilenceSettingsWindow).GetMethod("IsPositiveIntegerText", BindingFlags.Static | BindingFlags.NonPublic)!;
                    MethodInfo signedInput = typeof(SilenceSettingsWindow).GetMethod("IsSignedIntegerText", BindingFlags.Static | BindingFlags.NonPublic)!;
                    Assert((bool)positiveInput.Invoke(null, new object[] { "+123" })! && !(bool)positiveInput.Invoke(null, new object[] { "-1" })! && !(bool)positiveInput.Invoke(null, new object[] { "1.5" })! && !(bool)positiveInput.Invoke(null, new object[] { "abc" })!, "Positive parameter boxes accept only digits and optional leading plus sign");
                    Assert((bool)signedInput.Invoke(null, new object[] { "-45" })! && (bool)signedInput.Invoke(null, new object[] { "+45" })! && !(bool)signedInput.Invoke(null, new object[] { "4-5" })! && !(bool)signedInput.Invoke(null, new object[] { "-4.5" })!, "Silence threshold box accepts digits and one leading sign only");
                    var restoreButton = (Button)settingsWindow.FindName("RestoreDefaultsButton");
                    var cancelButton = (Button)settingsWindow.FindName("CancelButton");
                    var okButton = (Button)settingsWindow.FindName("OkButton");
                    Assert(Math.Abs(restoreButton.Width - 76) < 0.01, "Restore defaults button width is 76");
                    Assert(Math.Abs(cancelButton.Width - 54) < 0.01 && Math.Abs(okButton.Width - 54) < 0.01, "Cancel and OK button widths are 54");
                    settingsWindow.Close();
                    Assert(Math.Abs(window.FontSize - 12) < 0.01 && Math.Abs(timeText.FontSize - 12) < 0.01 && Math.Abs(list.FontSize - 12) < 0.01, "UI font size is 12");
                    Assert(Grid.GetColumn(slider) == 1 && Grid.GetColumn(timeText) == 2 && Grid.GetColumn(playButton) == 3, "Time display sits between progress slider and play button");
                    foreach (Button button in new[] { openButton, playButton, stopButton })
                    {
                        button.ApplyTemplate();
                        var border = (Border)button.Template.FindName("ButtonBorder", button);
                        var image = FindVisual<Image>(button)!;
                        Assert(Math.Abs(button.Width - 36) < 0.01 && Math.Abs(button.Height - 36) < 0.01 && Math.Abs(border.CornerRadius.TopLeft - 7) < 0.01, button.Name + " is a 36x36 rounded square");
                        Assert(Math.Abs(image.Width - 34) < 0.01 && Math.Abs(image.Height - 34) < 0.01, button.Name + " icon is 34x34");
                        Assert(button.HorizontalContentAlignment == HorizontalAlignment.Center && button.VerticalContentAlignment == VerticalAlignment.Center, button.Name + " content is centered");
                        Assert(image.HorizontalAlignment == HorizontalAlignment.Center && image.VerticalAlignment == VerticalAlignment.Center, button.Name + " icon is horizontally and vertically centered");
                        Assert(button.VerticalAlignment == VerticalAlignment.Center, button.Name + " is vertically centered in the top row");
                        Assert(button.FocusVisualStyle is null, button.Name + " has no focus visual");
                    }
                    Assert(list.FocusVisualStyle is null && slider.FocusVisualStyle is null, "DataGrid and slider have no focus visual");
                    Assert(slider.VerticalAlignment == VerticalAlignment.Center && timeText.VerticalAlignment == VerticalAlignment.Center, "Progress slider and time display are vertically centered");
                    Assert(list.RowHeight == 25, "List row height is 25");
                    Assert(list.ColumnHeaderHeight == 32, "List header height is 32");
                    Assert(list.Columns.Count == 4 && list.Items.Count == 3, "Four columns and three MP3 files");
                    list.UpdateLayout();
                    var row0 = (DataGridRow)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var row1 = (DataGridRow)list.ItemContainerGenerator.ContainerFromIndex(1);
                    var rowCell = FindVisual<DataGridCell>(row0)!;
                    Assert(row0.FocusVisualStyle is null && rowCell.FocusVisualStyle is null, "DataGrid rows and cells have no focus visual");
                    Assert(ColorOf(row0.Background) == "#FFFFFFFF" && ColorOf(row1.Background) == "#FFF0F0F0", "Alternating white and light grey rows");
                    list.SelectedIndex = 0;
                    await window.HandleShortcutAsync(Key.Space);
                    Task? analysis = Field<Task>(window, "analysisTask");
                    if (analysis is not null) await analysis;
                    var player = Field<AudioPlayer>(window, "player")!;
                    Assert(player.PlaybackState == PlaybackState.Playing, "Space opens selected track and plays");
                    playButton.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(ColorOf(row0.Background) == "#FFE0DFC6", "Playing row retains active color outside row focus");
                    var markerCell = FindVisual<DataGridCell>(row0)!;
                    var playingMark = FindVisual<System.Windows.Shapes.Path>(markerCell)!;
                    Point markerPosition = playingMark.TransformToAncestor(markerCell).Transform(new Point(0, 0));
                    Assert(playingMark.Visibility == Visibility.Visible && Math.Abs(markerPosition.X - (markerCell.ActualWidth - playingMark.ActualWidth) / 2) < 1, "Playing triangle is horizontally centered");
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(player.PlaybackState == PlaybackState.Paused, "Space pauses");
                    Assert(((AudioTrack)row0.Item).IsPaused && ColorOf(row0.Background) == "#FFE0DFC6", "Paused row retains bars and active color");
                    Assert(playingMark.Visibility == Visibility.Collapsed && FindVisual<StackPanel>(markerCell)!.Visibility == Visibility.Visible, "Pause replaces triangle with two bars");
                    var focusCell = FindVisual<DataGridCell>(row1)!;
                    focusCell.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(row1.IsKeyboardFocusWithin && ColorOf(row1.Background) == "#FFF2C867", "Focused row uses gold background");
                    Assert(ColorOf(row0.Background) == "#FFE0DFC6", "Paused row stays active when another row focused");
                    playButton.Focus();
                    double before = player.GetCurrentTime().TotalSeconds;
                    await window.HandleShortcutAsync(Key.Right);
                    Assert(player.PlaybackState == PlaybackState.Paused && player.GetCurrentTime().TotalSeconds > before, "Paused navigation remains paused");
                    var ranges = Field<List<SilenceRange>>(window, "ranges")!;
                    await player.SeekAsync(ranges[0].Middle);
                    await window.HandleShortcutAsync(Key.Space);
                    double first = player.GetCurrentTime().TotalSeconds;
                    Task a = window.HandleShortcutAsync(Key.Right);
                    Task b = window.HandleShortcutAsync(Key.Right);
                    await Task.WhenAll(a, b);
                    Assert(player.GetCurrentTime() >= ranges[2].Middle, "Two rapid presses advance two pauses");
                    await window.HandleShortcutAsync(Key.Space);
                    list.SelectedIndex = 1;
                    var loaded = Field<AudioTrack>(window, "loadedTrack")!;
                    Assert(!ReferenceEquals(list.SelectedItem, loaded), "Selection independent of loaded track");

                    playButton.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var sourceOutside = PresentationSource.FromVisual(window)!;
                    var outsideEnter = new KeyEventArgs(Keyboard.PrimaryDevice, sourceOutside, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    playButton.RaiseEvent(outsideEnter);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert(ReferenceEquals(Field<AudioTrack>(window, "loadedTrack"), loaded), "Enter outside the focused table row does not play the selected row");

                    var row1CellForEnter = FindVisual<DataGridCell>(row1)!;
                    row1CellForEnter.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var rowEnter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    row1CellForEnter.RaiseEvent(rowEnter);
                    Assert(rowEnter.Handled, "Focused table row consumes Enter");
                    Task? enterOperation = Field<Task>(window, "activeOperation");
                    if (enterOperation is not null) await enterOperation;
                    Task? enterAnalysis = Field<Task>(window, "analysisTask");
                    if (enterAnalysis is not null) await enterAnalysis;
                    var row1Track = (AudioTrack)row1.Item;
                    Assert(ReferenceEquals(Field<AudioTrack>(window, "loadedTrack"), row1Track) && player.PlaybackState == PlaybackState.Playing, "Enter on focused table row plays that row");

                    await player.SeekAsync(TimeSpan.FromSeconds(2));
                    row1CellForEnter.Focus();
                    var currentRowEnter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    row1CellForEnter.RaiseEvent(currentRowEnter);
                    Task? currentEnterOperation = Field<Task>(window, "activeOperation");
                    if (currentEnterOperation is not null) await currentEnterOperation;
                    Assert(player.PlaybackState == PlaybackState.Playing && player.GetCurrentTime() > TimeSpan.FromSeconds(1.8), "Enter on the already playing row does not restart it");

                    foreach (string name in new[] { "OpenButton", "PlayButton", "StopButton", "TrackList", "ProgressSlider" })
                    {
                        var control = (UIElement)window.FindName(name);
                        control.Focus();
                        foreach (Key key in new[] { Key.Space, Key.Left, Key.Right })
                        {
                            var source = PresentationSource.FromVisual(window)!;
                            var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                            control.RaiseEvent(down);
                            Assert(down.Handled, name + " consumes " + key + " down");
                            Task? operation = Field<Task>(window, "activeOperation");
                            if (operation is not null) await operation;
                            Task? navigation = Field<Task>(window, "navigationTask");
                            if (navigation is not null) await navigation;
                            await Dispatcher.Yield(DispatcherPriority.Background);
                            var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent };
                            control.RaiseEvent(up);
                            Assert(up.Handled, name + " consumes " + key + " up");
                        }
                    }
                    await player.SeekAsync(player.GetDuration() - TimeSpan.FromMilliseconds(50));
                    await player.PlayAsync();
                    var output = Field<WaveOutEvent>(player, "output")!;
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    // Delay UI dispatch to expose a pending natural-end callback.
                    while (output.PlaybackState != PlaybackState.Stopped && wait.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(5);
                    Assert(output.PlaybackState == PlaybackState.Stopped, "Audio reaches natural end");
                    await player.PlayAsync();
                    await Task.Delay(100);
                    Assert(player.PlaybackState == PlaybackState.Playing && player.GetCurrentTime() < TimeSpan.FromSeconds(2), "Restart survives pending natural-end callback");
                    player.Pause();
                    var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent };
                    slider.RaiseEvent(click);
                    Assert(FieldValue<bool>(window, "dragging"), "Track click starts seek interaction even when handled");
                    slider.Value = 5;
                    var release = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseUpEvent };
                    window.RaiseEvent(release);
                    Task? seek = Field<Task>(window, "activeOperation");
                    if (seek is not null) await seek;
                    Assert(Math.Abs(player.GetCurrentTime().TotalSeconds - 5) < 0.15 && !FieldValue<bool>(window, "dragging"), "Track click released outside slider commits seek");
                    await player.StopAsync();
                    Assert(player.GetCurrentTime() == TimeSpan.Zero, "Stop resets position");
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    playButton.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(!((AudioTrack)row0.Item).IsActive && ColorOf(row0.Background) == "#FFFFFFFF", "Stopped unfocused row restores original background");
                    window.UpdateLayout();
                    string folder = Path.GetFullPath("docs/test-results");
                    Directory.CreateDirectory(folder);
                    var content = (FrameworkElement)window.Content;
                    int width = (int)(content.ActualWidth + content.Margin.Left + content.Margin.Right);
                    int height = (int)(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                    {
                        drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                        drawing.DrawRectangle(new VisualBrush(content), null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
                    }
                    bitmap.Render(visual);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(folder, "window-preview.png"))) png.Save(stream);
                    window.Close();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(!window.IsVisible && !player.IsLoaded, "Window closes and releases audio");
                    foreach (string path in Directory.GetFiles(audioFolder, "*.mp3"))
                    {
                        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                        Assert(file.Length > 0, "File released: " + Path.GetFileName(path));
                    }
                    window = new MainWindow { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                    window.Show();
                    await window.LoadFolderAsync(audioFolder);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(FieldValue<bool>(window, "analyzing"), "Analysis in progress before close");
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert(!Field<AudioPlayer>(window, "player")!.IsLoaded, "Close during analysis releases player");
                    using (var file = new FileStream(Directory.GetFiles(audioFolder, "*.mp3")[0], FileMode.Open, FileAccess.Read, FileShare.None))
                        Assert(file.Length > 0, "Close during analysis releases analysis reader");
                }
                catch (Exception ex) { Console.WriteLine("FAIL UI: " + ex); failed++; }
                finally
                {
                    if (window is not null && window.IsVisible) { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
                    Console.WriteLine($"UI tests: {passed} passed, {failed} failed");
                    done.TrySetResult(failed);
                    app.Shutdown();
                }
            };
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static T? Field<T>(object instance, string name) where T : class
        => (T?)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static T FieldValue<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static string ColorOf(Brush brush) => ((SolidColorBrush)brush).Color.ToString();

    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            T? found = FindVisual<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}

