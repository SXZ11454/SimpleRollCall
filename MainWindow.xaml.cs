using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ModernWpf;
using ModernWpf.Controls;

namespace SimpleRollCall
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int DrawDurationMs = 3000;
        private static readonly Uri RepositoryUrl = new("https://github.com/SXZ11454/SimpleRollCall");

        private static readonly ResourceManager StringResources =
            new("SimpleRollCall.Properties.Resources", typeof(MainWindow).Assembly);

        private readonly AppConfig _config;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _stopwatch = new();
        private readonly Random _random = new();
        private readonly List<string> _names = new();
        private List<TextBlock> _slots = new();
        private string[] _finalNames = Array.Empty<string>();
        private double[] _stopTimesMs = Array.Empty<double>();
        private bool[] _stopped = Array.Empty<bool>();
        private bool _drawing;
        private bool _pausing;
        private readonly HashSet<string> _memory = new(); // Machine learning: drawn people (temporary memory list)

        public MainWindow()
        {
            InitializeComponent();

            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* falls back to utf-8 if gb18030 is unavailable */ }

            _config = AppConfig.Load();
            PeopleCountBox.Value = _config.DrawCount;
            ApplyTheme();
            CultureInfo.CurrentUICulture = _config.GetCulture();
            ApplyLocalization();
            LoadNames();

            // Settings use ContentDialog; a fresh dialog instance and panel are created per show
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += Timer_Tick;

            Closed += (_, _) => _timer.Stop();
        }

        private static string L(string key) =>
            StringResources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

        // ---------- Draw ----------

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_drawing)
            {
                // While drawing: the play button becomes pause; pressing it enters the stop sequence
                PauseDraw();
                return;
            }

            // First launch or no names file detected: show a notice (confirm only),
            // then open the settings screen after confirmation
            if (_names.Count == 0)
            {
                await ShowNoticeAsync(L("NoNames"));
                OpenSettings();
                return;
            }

            StartDraw();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetToWaiting();

        private void StartDraw()
        {
            int count = GetDrawCount();
            _finalNames = PickNames(count);
            _stopped = new bool[count];
            _stopTimesMs = new double[count];

            if (_config.AutoStop)
            {
                // Auto stop: determinate progress bar; single draw stops at exactly 3 seconds,
                // multi draw stops slots 1..N progressively between 2.1s and 3.0s
                DrawProgress.IsIndeterminate = false;
                DrawProgress.Value = 0;
                if (count == 1)
                    _stopTimesMs[0] = DrawDurationMs;
                else
                    for (int i = 0; i < count; i++)
                        _stopTimesMs[i] = 2100 + 900.0 * i / (count - 1);
            }
            else
            {
                // Manual pause: indeterminate progress bar keeps scrolling until pause is pressed
                DrawProgress.IsIndeterminate = true;
                for (int i = 0; i < count; i++)
                    _stopTimesMs[i] = double.MaxValue;
            }

            if (count == 1)
            {
                SingleText.Visibility = Visibility.Visible;
                MultiPanel.Visibility = Visibility.Collapsed;
                SingleText.Text = _names[_random.Next(_names.Count)];
            }
            else
            {
                SingleText.Visibility = Visibility.Collapsed;
                MultiPanel.Visibility = Visibility.Visible;
                MultiPanel.Children.Clear();
                _slots = new List<TextBlock>();
                for (int i = 0; i < count; i++)
                {
                    var slot = new TextBlock
                    {
                        FontSize = 48,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(16, 8, 16, 8),
                        Text = _names[_random.Next(_names.Count)]
                    };
                    _slots.Add(slot);
                    MultiPanel.Children.Add(slot);
                }
            }

            _drawing = true;
            _pausing = false;
            SetPlayIcon(false); // play button switches to pause
            _stopwatch.Restart();
            _timer.Start();
        }

        private void PauseDraw()
        {
            if (_pausing)
                return;

            _pausing = true;
            double now = _stopwatch.Elapsed.TotalMilliseconds;

            if (_config.AutoStop)
            {
                // Auto stop mode: enter the stop sequence — slot 1 stops immediately, each
                // following slot 0.1s later; keep any earlier scheduled stop time
                for (int i = 0; i < _stopTimesMs.Length; i++)
                    _stopTimesMs[i] = Math.Min(_stopTimesMs[i], now + i * 100.0);
            }
            else
            {
                // Manual mode: pause stops all slots immediately (no delay)
                for (int i = 0; i < _stopTimesMs.Length; i++)
                    _stopTimesMs[i] = now;
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            double elapsed = _stopwatch.Elapsed.TotalMilliseconds;

            if (_config.AutoStop && !_pausing)
                DrawProgress.Value = Math.Min(100, elapsed / DrawDurationMs * 100);

            bool isSingle = _finalNames.Length == 1;
            bool allStopped = true;
            for (int i = 0; i < _finalNames.Length; i++)
            {
                if (_stopped[i])
                    continue;

                // Keep showing random names
                string rolling = _names[_random.Next(_names.Count)];
                if (isSingle) SingleText.Text = rolling;
                else _slots[i].Text = rolling;

                // This slot reached its stop time; lock in the final result
                if (elapsed >= _stopTimesMs[i])
                {
                    _stopped[i] = true;
                    if (isSingle) SingleText.Text = _finalNames[0];
                    else _slots[i].Text = _finalNames[i];
                }
                else
                {
                    allStopped = false;
                }
            }

            // Safety net: force the stop sequence at 3 seconds in auto stop mode
            if (_config.AutoStop && !_pausing && elapsed >= DrawDurationMs)
                PauseDraw();

            if (allStopped)
                FinishDraw();
        }

        private void FinishDraw()
        {
            _timer.Stop();
            _stopwatch.Stop();
            _drawing = false;
            _pausing = false;
            DrawProgress.IsIndeterminate = false;
            DrawProgress.Value = 100;
            SetPlayIcon(true);

            // Machine learning: drawn people join the memory list; once everyone has been
            // drawn, the memory is cleared and all names become drawable again
            if (_config.MachineLearning)
            {
                foreach (var name in _finalNames)
                    _memory.Add(name);
                if (_memory.Count >= _names.Count)
                    _memory.Clear();
            }
        }

        private void ResetToWaiting()
        {
            _timer.Stop();
            _stopwatch.Reset();
            _drawing = false;
            _pausing = false;
            DrawProgress.IsIndeterminate = false;
            DrawProgress.Value = 0;
            SetPlayIcon(true);
            MultiPanel.Children.Clear();
            MultiPanel.Visibility = Visibility.Collapsed;
            SingleText.Visibility = Visibility.Visible;
            SingleText.Text = L("Waiting");
        }

        private int GetDrawCount() =>
            Math.Clamp((int)Math.Round(PeopleCountBox.Value), 1, 10);

        private string[] PickNames(int count)
        {
            // Machine learning: only draw from names outside the memory list; when the
            // memory covers the whole roster, clear it and draw from all names again
            var pool = new List<string>();
            foreach (var name in _names)
                if (!_config.MachineLearning || !_memory.Contains(name))
                    pool.Add(name);

            if (pool.Count == 0)
            {
                _memory.Clear();
                pool.AddRange(_names);
            }

            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = pool[i % pool.Count];
            return result;
        }

        private void SetPlayIcon(bool play)
        {
            if (PlayButton.Content is SymbolIcon icon)
                icon.Symbol = play ? Symbol.Play : Symbol.Pause;
            PlayButton.ToolTip = L(play ? "StartTooltip" : "PauseTooltip");
        }

        // ---------- Settings ----------

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

        // Settings dialog control fields (the panel is rebuilt per show and used exactly
        // once, avoiding parent conflicts from reusing elements across dialog instances)
        private ComboBox? _languageCombo;
        private ComboBox? _themeCombo;
        private ComboBox? _encodingCombo;
        private ModernWpf.Controls.ToggleSwitch? _autoStopSwitch;
        private ModernWpf.Controls.ToggleSwitch? _machineLearningSwitch;
        private TextBox? _filePathBox;

        private async void OpenSettings()
        {
            var panel = BuildSettingsPanel();
            var dialog = new ContentDialog
            {
                Owner = this, // Explicit owner: prevents ShowAsync from hanging on GetActiveWindow when the window is briefly inactive
                Title = L("Settings"),
                PrimaryButtonText = L("Confirm"),
                Content = panel
            };
            dialog.PrimaryButtonClick += SettingsDialog_PrimaryButtonClick;
            await dialog.ShowAsync();
        }

        private StackPanel BuildSettingsPanel()
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock { Text = L("Language"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            _languageCombo = new ComboBox { SelectedIndex = _config.Language == "en" ? 1 : 0, Margin = new Thickness(0, 0, 0, 12) };
            _languageCombo.Items.Add(new ComboBoxItem { Content = "中文" });
            _languageCombo.Items.Add(new ComboBoxItem { Content = "English" });
            panel.Children.Add(_languageCombo);

            panel.Children.Add(new TextBlock { Text = L("Theme"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            _themeCombo = new ComboBox { SelectedIndex = _config.Theme switch { "light" => 1, "dark" => 2, _ => 0 }, Margin = new Thickness(0, 0, 0, 12) };
            _themeCombo.Items.Add(new ComboBoxItem { Content = L("ThemeSystem") });
            _themeCombo.Items.Add(new ComboBoxItem { Content = L("ThemeLight") });
            _themeCombo.Items.Add(new ComboBoxItem { Content = L("ThemeDark") });
            panel.Children.Add(_themeCombo);

            panel.Children.Add(new TextBlock { Text = L("Encoding"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            _encodingCombo = new ComboBox { SelectedIndex = _config.EncodingName == "gb18030" ? 1 : 0, Margin = new Thickness(0, 0, 0, 12) };
            _encodingCombo.Items.Add(new ComboBoxItem { Content = "UTF-8" });
            _encodingCombo.Items.Add(new ComboBoxItem { Content = "GB18030" });
            panel.Children.Add(_encodingCombo);

            var autoStopPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            _autoStopSwitch = new ModernWpf.Controls.ToggleSwitch { OnContent = "", OffContent = "", IsOn = _config.AutoStop, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            autoStopPanel.Children.Add(_autoStopSwitch);
            DockPanel.SetDock(_autoStopSwitch, Dock.Right);
            autoStopPanel.Children.Add(new TextBlock { Text = L("AutoStop"), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(autoStopPanel);

            var mlPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            _machineLearningSwitch = new ModernWpf.Controls.ToggleSwitch { OnContent = "", OffContent = "", IsOn = _config.MachineLearning, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            mlPanel.Children.Add(_machineLearningSwitch);
            DockPanel.SetDock(_machineLearningSwitch, Dock.Right);
            mlPanel.Children.Add(new TextBlock { Text = L("MachineLearning"), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(mlPanel);

            panel.Children.Add(new TextBlock { Text = L("File"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var filePanel = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var clearButton = new Button { Content = L("ClearFile"), Margin = new Thickness(8, 0, 0, 0) };
            clearButton.Click += (_, _) => _filePathBox.Text = "";
            filePanel.Children.Add(clearButton);
            DockPanel.SetDock(clearButton, Dock.Right);
            var browseButton = new Button { Content = L("Browse"), Margin = new Thickness(8, 0, 0, 0) };
            browseButton.Click += (_, _) => _filePathBox.Text = PickNamesFile();
            filePanel.Children.Add(browseButton);
            DockPanel.SetDock(browseButton, Dock.Right);
            _filePathBox = new TextBox { IsReadOnly = true, Text = _config.NamesFile, VerticalContentAlignment = VerticalAlignment.Center };
            filePanel.Children.Add(_filePathBox);
            panel.Children.Add(filePanel);

            panel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 8) });
            panel.Children.Add(new TextBlock { Text = L("Version"), FontSize = 12, Opacity = 0.6 });
            panel.Children.Add(new TextBlock { Text = L("Copyright"), FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = L("License"), FontSize = 12, Opacity = 0.6 });

            var repoText = new TextBlock { FontSize = 12, Opacity = 0.6 };
            var repoLink = new Hyperlink { NavigateUri = RepositoryUrl };
            repoLink.Inlines.Add(L("Repository"));
            repoLink.RequestNavigate += (_, e) =>
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            repoText.Inlines.Add(repoLink);
            panel.Children.Add(repoText);

            return panel;
        }

        private string PickNamesFile()
        {
            var dialog = new OpenFileDialog { Filter = L("TextFileFilter"), Title = L("File") };
            return dialog.ShowDialog() == true ? dialog.FileName : _filePathBox.Text;
        }

        private void SettingsDialog_PrimaryButtonClick(object sender, ContentDialogButtonClickEventArgs e)
        {
            if (_languageCombo is null || _themeCombo is null || _encodingCombo is null ||
                _autoStopSwitch is null || _machineLearningSwitch is null || _filePathBox is null)
                return;

            _config.Language = _languageCombo.SelectedIndex == 1 ? "en" : "zh";
            _config.Theme = _themeCombo.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            _config.EncodingName = _encodingCombo.SelectedIndex == 1 ? "gb18030" : "utf-8";
            _config.AutoStop = _autoStopSwitch.IsOn;
            _config.MachineLearning = _machineLearningSwitch.IsOn;
            _config.NamesFile = _filePathBox.Text.Trim();
            _config.DrawCount = GetDrawCount();
            _config.Save();

            CultureInfo.CurrentUICulture = _config.GetCulture();
            ApplyTheme();
            ApplyLocalization();
            LoadNames();
            if (!_drawing)
                ResetToWaiting();
        }

        private void ApplyTheme()
        {
            ThemeManager.Current.ApplicationTheme = _config.Theme switch
            {
                "light" => ApplicationTheme.Light,
                "dark" => ApplicationTheme.Dark,
                _ => null // follow the system theme
            };
        }

        private void LoadNames()
        {
            _names.Clear();
            _memory.Clear(); // clear the memory list when the roster changes
            if (string.IsNullOrWhiteSpace(_config.NamesFile) || !File.Exists(_config.NamesFile))
                return;

            try
            {
                var encoding = _config.EncodingName == "gb18030" ? Encoding.GetEncoding("gb18030") : Encoding.UTF8;
                foreach (var line in File.ReadAllLines(_config.NamesFile, encoding))
                    if (!string.IsNullOrWhiteSpace(line))
                        _names.Add(line.Trim());
            }
            catch
            {
                _names.Clear(); // treat read failures as an empty roster
            }
        }

        // ---------- Localization ----------

        private void ApplyLocalization()
        {
            Title = L("WindowTitle");

            if (!_drawing)
            {
                SingleText.Visibility = Visibility.Visible;
                MultiPanel.Visibility = Visibility.Collapsed;
                SingleText.Text = L("Waiting");
            }

            PlayButton.ToolTip = L(_drawing ? "PauseTooltip" : "StartTooltip");
            ResetButton.ToolTip = L("ResetTooltip");
            PeopleButton.ToolTip = L("MultiAccountTooltip");
            SettingsButton.ToolTip = L("SettingsTooltip");
            PeopleCountLabel.Text = L("PeopleCount");
            // The settings panel is rebuilt with L() on every show, no update needed here
        }

        private async Task ShowNoticeAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Owner = this,
                Title = L("Notice"),
                Content = message,
                CloseButtonText = L("Confirm")
            };
            await dialog.ShowAsync();
        }
    }
}
