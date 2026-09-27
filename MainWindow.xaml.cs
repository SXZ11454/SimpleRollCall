using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ModernWpf;
using ModernWpf.Controls;
using Res = SimpleRollCall.Properties.Resources;

namespace SimpleRollCall
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int DrawDurationMs = 3000;

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
        private bool _transitioning; // true while the settings page slides in or out
        private readonly HashSet<string> _memory = new(); // Machine learning: drawn people (temporary memory list)

        public MainWindow()
        {
            InitializeComponent();

            _config = AppConfig.Current;
            PeopleCountBox.Value = _config.DrawCount;
            // Subscribe after the initial value is restored so startup does not rewrite the config
            PeopleCountBox.ValueChanged += PeopleCountBox_ValueChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += Timer_Tick;

            // The title bar back button (only shown while the settings page is open)
            var backCommand = new RelayCommand(CloseSettings);
            TitleBar.SetBackButtonCommand(this, backCommand);
            InputBindings.Add(new KeyBinding(backCommand, Key.Escape, ModifierKeys.None));

            ApplySettings();

            Closed += (_, _) => _timer.Stop();
        }

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
                await ShowNoticeAsync(Res.NoNames);
                OpenSettings();
                return;
            }

            _finalNames = PickNames(GetDrawCount());
            if (_finalNames.Length == 0)
            {
                // Every person is filtered out by the odds rules (all weights are 0)
                await ShowNoticeAsync(Res.OddsNoOne);
                return;
            }

            StartDraw();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetToWaiting();

        private void StartDraw()
        {
            int count = _finalNames.Length;
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

            // Machine learning: drawn people join the memory list; once everybody who
            // can still be drawn has been drawn, the memory is cleared and everybody
            // becomes drawable again. Weight 0 people never take part and weight 100
            // people bypass the memory, so only the people in between have to be covered.
            if (_config.MachineLearning)
            {
                foreach (var name in _finalNames)
                    _memory.Add(name);

                bool pending = false;
                foreach (var name in _names)
                {
                    int weight = GetWeight(name);
                    if (weight > 0 && weight < 100 && !_memory.Contains(name))
                    {
                        pending = true;
                        break;
                    }
                }

                if (!pending)
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
            SingleText.Text = Res.Waiting;
        }

        private int GetDrawCount() =>
            Math.Clamp((int)Math.Round(PeopleCountBox.Value), 1, 10);

        private void PeopleCountBox_ValueChanged(object sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs e)
        {
            int count = GetDrawCount();
            if (_config.DrawCount == count)
                return;

            _config.DrawCount = count;
            _config.Save();
        }

        /// <summary>The roster, used by the odds table to offer the known people.</summary>
        internal IReadOnlyList<string> Names => _names;

        /// <summary>
        /// Effective weight of a name according to the odds rules. A rule only counts
        /// for people that are really in the roster; disabled rules and people without
        /// a rule fall back to the normal weight of 50.
        /// </summary>
        private int GetWeight(string name)
        {
            foreach (var rule in _config.OddsRules)
            {
                if (!rule.Enabled)
                    continue;
                if (!string.Equals(rule.Name.Trim(), name, StringComparison.Ordinal))
                    continue;
                return rule.Weight;
            }

            return OddsRule.NormalWeight;
        }

        /// <summary>People that may take part in the draw, paired with their weight.</summary>
        private List<(string Name, int Weight)> BuildPool(bool useMemory)
        {
            var pool = new List<(string, int)>();
            foreach (var name in _names)
            {
                int weight = GetWeight(name);
                if (weight <= 0)
                    continue; // 爆率 0: this person is never drawn
                if (useMemory && weight < 100 && _memory.Contains(name))
                    continue; // machine learning: drawn recently
                pool.Add((name, weight));
            }

            return pool;
        }

        private string[] PickNames(int count)
        {
            var pool = BuildPool(useMemory: true);

            if (pool.Count == 0)
            {
                // Machine learning excluded everybody: start a fresh round
                _memory.Clear();
                pool = BuildPool(useMemory: false);
            }

            if (pool.Count == 0)
                return Array.Empty<string>(); // every weight is 0: nobody may be drawn

            var result = new List<string>(count);
            while (result.Count < count)
                DrawCycle(pool, Math.Min(count - result.Count, pool.Count), result);
            return result.ToArray();
        }

        /// <summary>
        /// Picks up to <paramref name="slots"/> people from the pool in one round:
        /// weight 100 people are placed first (they are always drawn), the remaining
        /// slots are drawn proportionally to the weights.
        /// </summary>
        private void DrawCycle(List<(string Name, int Weight)> pool, int slots, List<string> result)
        {
            var remaining = new List<(string Name, int Weight)>(pool);

            var guaranteed = new List<string>();
            foreach (var entry in remaining)
                if (entry.Weight >= 100)
                    guaranteed.Add(entry.Name);
            Shuffle(guaranteed);

            foreach (var name in guaranteed)
            {
                if (slots == 0)
                    break;
                result.Add(name);
                slots--;
                remaining.RemoveAll(entry => entry.Name == name);
            }

            while (slots > 0 && remaining.Count > 0)
            {
                int total = 0;
                foreach (var entry in remaining)
                    total += entry.Weight;

                int roll = _random.Next(total);
                int index = 0;
                int accumulated = 0;
                for (; index < remaining.Count - 1; index++)
                {
                    accumulated += remaining[index].Weight;
                    if (roll < accumulated)
                        break;
                }

                result.Add(remaining[index].Name);
                remaining.RemoveAt(index);
                slots--;
            }
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void SetPlayIcon(bool play)
        {
            if (PlayButton.Content is SymbolIcon icon)
                icon.Symbol = play ? Symbol.Play : Symbol.Pause;
            PlayButton.ToolTip = play ? Res.StartTooltip : Res.PauseTooltip;
        }

        // ---------- Settings page (full screen, hosted in RootFrame) ----------

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

        /// <summary>
        /// Shows the settings page on top of the whole window.
        /// A plain page inside a frame replaces the old ContentDialog, so there is no
        /// ShowAsync (and therefore no waiting on an active window) that could hang.
        /// </summary>
        internal void OpenSettings()
        {
            if (_transitioning || RootFrame.Visibility == Visibility.Visible)
                return;

            var page = new SettingsPage(_config, this);
            RootFrame.Content = page;
            RootFrame.Visibility = Visibility.Visible;
            ApplyTitleBar();
            AnimatePage(page, entering: true, onCompleted: null);
        }

        /// <summary>Called by the title bar back button (or Escape) of the settings page.</summary>
        internal void CloseSettings()
        {
            if (_transitioning || RootFrame.Visibility != Visibility.Visible)
                return;

            if (RootFrame.Content is FrameworkElement page)
            {
                // Slide the page away first, then drop it (drill-out, mirror of AnimatePage)
                _transitioning = true;
                AnimatePage(page, entering: false, onCompleted: () =>
                {
                    RootFrame.Content = null;
                    RootFrame.Visibility = Visibility.Collapsed;
                    ApplyTitleBar();
                    _transitioning = false;
                });
            }
            else
            {
                RootFrame.Content = null;
                RootFrame.Visibility = Visibility.Collapsed;
                ApplyTitleBar();
            }
        }

        /// <summary>Rebuilds the visible settings page, used after the language changed.</summary>
        internal void ReloadSettings()
        {
            if (RootFrame.Visibility == Visibility.Visible)
                RootFrame.Content = new SettingsPage(_config, this);
        }

        /// <summary>
        /// UWP style enter/leave animation: the page zooms in (grows from 90% to 100%
        /// while fading in) when it opens and zooms back out when it closes.
        /// </summary>
        private static void AnimatePage(FrameworkElement page, bool entering, Action? onCompleted)
        {
            const double zoom = 0.9; // scale the page starts from (enter) / shrinks to (leave)
            const int durationMs = 220;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            var scale = new ScaleTransform(entering ? zoom : 1, entering ? zoom : 1);
            page.RenderTransformOrigin = new Point(0.5, 0.5); // zoom around the page centre
            page.RenderTransform = scale;
            page.Opacity = entering ? 0 : 1;

            var fade = new DoubleAnimation
            {
                From = page.Opacity,
                To = entering ? 1 : 0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            var growX = new DoubleAnimation
            {
                From = scale.ScaleX,
                To = entering ? 1 : zoom,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            var growY = new DoubleAnimation
            {
                From = scale.ScaleY,
                To = entering ? 1 : zoom,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            Storyboard.SetTarget(fade, page);
            Storyboard.SetTargetProperty(fade, new PropertyPath(FrameworkElement.OpacityProperty));
            Storyboard.SetTarget(growX, page);
            Storyboard.SetTargetProperty(growX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            Storyboard.SetTarget(growY, page);
            Storyboard.SetTargetProperty(growY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Children.Add(growX);
            storyboard.Children.Add(growY);
            storyboard.Completed += (_, _) =>
            {
                page.RenderTransform = Transform.Identity;
                onCompleted?.Invoke();
            };
            storyboard.Begin();
        }

        /// <summary>
        /// Title bar state while the settings page is shown: back button at the top-left
        /// corner and the page name as the window title (colour stays theme default).
        /// </summary>
        private void ApplyTitleBar()
        {
            bool inSettings = RootFrame.Visibility == Visibility.Visible;
            TitleBar.SetIsBackButtonVisible(this, inSettings);
            Title = inSettings ? Res.Settings : Res.WindowTitle;
        }

        /// <summary>Applies the current configuration to the running application.</summary>
        internal void ApplySettings()
        {
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
            ApplyTitleBar(); // window title: "settings" while the settings page is open

            if (!_drawing)
            {
                SingleText.Visibility = Visibility.Visible;
                MultiPanel.Visibility = Visibility.Collapsed;
                SingleText.Text = Res.Waiting;
            }

            PlayButton.ToolTip = _drawing ? Res.PauseTooltip : Res.StartTooltip;
            ResetButton.ToolTip = Res.ResetTooltip;
            PeopleButton.ToolTip = Res.MultiAccountTooltip;
            SettingsButton.ToolTip = Res.SettingsTooltip;
            PeopleCountLabel.Text = Res.PeopleCount;
            // The settings page is rebuilt from {x:Static} resources every time it opens
        }

        private async Task ShowNoticeAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Owner = this,
                Title = Res.Notice,
                Content = message,
                CloseButtonText = Res.Confirm
            };
            await dialog.ShowAsync();
        }
    }
}
