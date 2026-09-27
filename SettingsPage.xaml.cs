using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using Res = SimpleRollCall.Properties.Resources;

namespace SimpleRollCall
{
    /// <summary>
    /// Full screen settings page (UWP style). Every control writes straight back to
    /// <see cref="AppConfig"/> and applies the change right away, so no confirm button
    /// and no dialog are needed.
    /// </summary>
    public partial class SettingsPage : Page
    {
        private readonly AppConfig _config;
        private readonly MainWindow _mainWindow;

        // Suppresses the change handlers while the controls are filled with the saved values
        private bool _loading = true;

        // The row the remove button acts on; watched so the button follows its name
        private OddsRule? _selectedRule;

        public SettingsPage(AppConfig config, MainWindow mainWindow)
        {
            _config = config;
            _mainWindow = mainWindow;
            InitializeComponent();

            // The odds table edits the live rule list, which the draw reads on every start
            RulesGrid.ItemsSource = _config.OddsRules;

            // The table never shows an empty grid: if the config holds no rule yet,
            // start with one blank row the user can type into right away.
            EnsureRowExists();

            LanguageCombo.SelectedIndex = _config.Language == "en" ? 1 : 0;
            ThemeCombo.SelectedIndex = _config.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
            EncodingCombo.SelectedIndex = _config.EncodingName == "gb18030" ? 1 : 0;
            AutoStopSwitch.IsOn = _config.AutoStop;
            MachineLearningSwitch.IsOn = _config.MachineLearning;
            FilePathBox.Text = _config.NamesFile;

            _loading = false;
        }

        // ---------- Navigation ----------

        /// <summary>The gear button in the pinned header: back to the main page.</summary>
        private void HomeButton_Click(object sender, RoutedEventArgs e) => _mainWindow.CloseSettings();

        private void RepositoryLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                /* no browser available: ignore */
            }
        }

        // ---------- Settings (applied immediately) ----------

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            _config.Language = LanguageCombo.SelectedIndex == 1 ? "en" : "zh";
            Apply();

            // The page texts come from {x:Static}, which is evaluated once per instance:
            // rebuild the page so every string follows the new language immediately.
            _mainWindow.ReloadSettings();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            _config.Theme = ThemeCombo.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            Apply();
        }

        private void EncodingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            _config.EncodingName = EncodingCombo.SelectedIndex == 1 ? "gb18030" : "utf-8";
            Apply();
        }

        private void AutoStopSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            _config.AutoStop = AutoStopSwitch.IsOn;
            _config.Save();
        }

        private void MachineLearningSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            _config.MachineLearning = MachineLearningSwitch.IsOn;
            _config.Save();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = Res.TextFileFilter, Title = Res.File };
            if (dialog.ShowDialog(_mainWindow) == true)
                SetNamesFile(dialog.FileName);
        }

        private void ClearFileButton_Click(object sender, RoutedEventArgs e) => SetNamesFile("");

        private void SetNamesFile(string path)
        {
            FilePathBox.Text = path;
            _config.NamesFile = path.Trim();
            Apply();
        }

        // ---------- Odds (weight) table ----------

        /// <summary>The roster offered by the person column of the odds table.</summary>
        public IReadOnlyList<string> Roster => _mainWindow.Names;

        /// <summary>Right click selects the row under the cursor, so the menu hits the right one.</summary>
        private void RulesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<DataGridRow>(source) is { } row)
            {
                row.IsSelected = true;
            }
            else
            {
                RulesGrid.SelectedItem = null; // outside a row: nothing to delete
            }
        }

        private void RulesGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && RulesGrid.SelectedItem is OddsRule rule)
            {
                DeleteRule(rule);
                e.Handled = true;
            }
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is OddsRule rule)
                DeleteRule(rule);
        }

        /// <summary>
        /// Clicking the background of the enabled cell flips its switch, like ticking
        /// a check box. Clicks that land on the switch itself are left alone: the
        /// switch toggles when its thumb is released, so flipping it here as well
        /// would cancel the two toggles out.
        /// </summary>
        private void OddsEnabledCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject hit &&
                FindAncestor<ModernWpf.Controls.ToggleSwitch>(hit) is not null)
            {
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is OddsRule rule)
            {
                rule.Enabled = !rule.Enabled;
            }
        }

        // ---------- Keeping the table alive ----------

        /// <summary>
        /// Removes one rule - unless it is the single blank row, which stays: that row
        /// is the table's "new row" and there always has to be one. Removing the final
        /// rule therefore brings a fresh blank row back immediately.
        /// </summary>
        private void DeleteRule(OddsRule rule)
        {
            if (IsLastEmptyRow(rule))
                return;

            _config.OddsRules.Remove(rule);
            EnsureRowExists();
        }

        /// <summary>True when this is the one empty row the table is not allowed to lose.</summary>
        private bool IsLastEmptyRow(OddsRule rule) =>
            _config.OddsRules.Count == 1 && string.IsNullOrWhiteSpace(rule.Name);

        /// <summary>
        /// Keeps at least one row in the table: an empty rule list is turned into a
        /// single blank row (all controls, no content). Blank rows are not written to
        /// the config file, so this never counts as a rule.
        /// </summary>
        private void EnsureRowExists()
        {
            if (_config.OddsRules.Count == 0)
                _config.OddsRules.Add(new OddsRule());
        }

        // ---------- Row buttons below the table ----------

        /// <summary>
        /// Appends an empty rule and puts the caret into its person box so the row can
        /// be filled in right away; the config hooks save at once, and the row only
        /// reaches the file once it carries a person.
        /// </summary>
        private void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            var rule = new OddsRule();
            _config.OddsRules.Add(rule);

            RulesGrid.SelectedItem = rule;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RulesGrid.UpdateLayout();
                if (RulesGrid.ItemContainerGenerator.ContainerFromItem(rule) is DataGridRow row)
                {
                    row.IsSelected = true;
                    if (FindChild<ComboBox>(row) is { } box)
                    {
                        box.Focus();
                    }

                    // The table itself never scrolls, so ask the page's ScrollViewer
                    // to bring the new row into view
                    row.BringIntoView();
                }
            }), DispatcherPriority.Loaded);
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is OddsRule rule)
                DeleteRule(rule);
        }

        /// <summary>
        /// Tracks the selected rule: the remove button is only enabled while that row
        /// may actually be deleted (so the single blank row shows a dead - button), and
        /// the button follows the name while it is being typed or cleared.
        /// </summary>
        private void RulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedRule is not null)
                _selectedRule.PropertyChanged -= SelectedRule_PropertyChanged;

            _selectedRule = RulesGrid.SelectedItem as OddsRule;

            if (_selectedRule is not null)
                _selectedRule.PropertyChanged += SelectedRule_PropertyChanged;

            UpdateRemoveButton();
        }

        private void SelectedRule_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Only the person makes a row "content": clearing it locks the row again.
            if (e.PropertyName is nameof(OddsRule.Name) or null)
                UpdateRemoveButton();
        }

        private void UpdateRemoveButton() =>
            RemoveRuleButton.IsEnabled = _selectedRule is { } rule && !IsLastEmptyRow(rule);

        /// <summary>
        /// The page is rebuilt whenever the language changes; drop the handler so the
        /// old instance does not stay alive through the selected rule.
        /// </summary>
        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_selectedRule is not null)
            {
                _selectedRule.PropertyChanged -= SelectedRule_PropertyChanged;
                _selectedRule = null;
            }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is not Visual and not System.Windows.Media.Media3D.Visual3D)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                if (FindChild<T>(child) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
        {
            for (var current = element; current is not null; current = GetParent(current))
                if (current is T match)
                    return match;
            return null;
        }

        private static DependencyObject? GetParent(DependencyObject obj) =>
            obj is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(obj)
                : obj is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(obj);

        private void Apply()
        {
            _config.Save();
            _mainWindow.ApplySettings(); // theme, language and name list take effect at once
        }
    }
}
