using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace SimpleRollCall
{
    /// <summary>
    /// Application settings, persisted to SRC_Config.ini next to the executable.
    /// </summary>
    public class AppConfig
    {
        public string Language { get; set; } = "zh";      // zh | en
        public string Theme { get; set; } = "system";     // system | light | dark
        public string EncodingName { get; set; } = "utf-8"; // utf-8 | gb18030
        public string NamesFile { get; set; } = "";       // Path to the names txt file
        public int DrawCount { get; set; } = 1;           // Number of people to draw, 1-10
        public bool AutoStop { get; set; } = false;       // Auto stop: end the draw automatically after 3 seconds
        public bool MachineLearning { get; set; } = false; // Machine learning: drawn people enter a memory list and are not drawn again

        /// <summary>
        /// Odds (爆率) rules, one per table row. They are read on every draw, so the
        /// grid edits take effect immediately; changes are written back by the
        /// change hooks attached in <see cref="AttachOddsRules"/>.
        /// </summary>
        public ObservableCollection<OddsRule> OddsRules { get; } = new();

        /// <summary>
        /// The single configuration instance shared by the whole application
        /// (loaded once, so every screen reads and writes the same settings).
        /// </summary>
        public static AppConfig Current { get; } = Load();

        public static string ConfigPath =>
            Path.Combine(AppDirectory, "SRC_Config.ini");

        // Environment.ProcessPath is the real exe location; AppContext.BaseDirectory
        // would point to the single-file extraction temp directory.
        public static string AppDirectory =>
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        public static AppConfig Load()
        {
            var config = new AppConfig();
            if (!File.Exists(ConfigPath))
                return config;

            foreach (var line in File.ReadAllLines(ConfigPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("[") || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;

                int index = trimmed.IndexOf('=');
                if (index <= 0)
                    continue;

                var key = trimmed[..index].Trim();
                var value = trimmed[(index + 1)..].Trim();
                switch (key)
                {
                    case "Language": config.Language = value; break;
                    case "Theme": config.Theme = value; break;
                    case "Encoding": config.EncodingName = value; break;
                    case "File": config.NamesFile = value; break;
                    case "DrawCount" when int.TryParse(value, out var count):
                        config.DrawCount = Math.Clamp(count, 1, 10);
                        break;
                    case "AutoStop":
                        config.AutoStop = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
                        break;
                    case "MachineLearning":
                        config.MachineLearning = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
                        break;
                    case "Odds":
                        config.AddOddsRule(value);
                        break;
                }
            }
            config.AttachOddsRules();
            return config;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Settings]");
            sb.AppendLine($"Language={Language}");
            sb.AppendLine($"Theme={Theme}");
            sb.AppendLine($"Encoding={EncodingName}");
            sb.AppendLine($"File={NamesFile}");
            sb.AppendLine($"DrawCount={DrawCount}");
            sb.AppendLine($"AutoStop={AutoStop}");
            sb.AppendLine($"MachineLearning={MachineLearning}");
            foreach (var rule in OddsRules)
            {
                // A row without a person is not a rule - it is only the blank row the
                // table keeps so there is always something to type into. Leaving it out
                // keeps the file clean and stops an untouched table from rewriting it.
                if (string.IsNullOrWhiteSpace(rule.Name))
                    continue;
                sb.AppendLine($"Odds={rule.Weight}\t{(rule.Enabled ? "1" : "0")}\t{rule.Name}");
            }
            File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Reads one <c>Odds=weight \t enabled \t name</c> line. The name goes last
        /// so it may contain '=' characters; anything malformed is skipped.
        /// </summary>
        private void AddOddsRule(string value)
        {
            var parts = value.Split('\t');
            if (parts.Length < 3 || !int.TryParse(parts[0].Trim(), out var weight))
                return;

            var enabled = parts[1].Trim();
            OddsRules.Add(new OddsRule
            {
                Weight = weight,
                Enabled = enabled == "1" || enabled.Equals("true", StringComparison.OrdinalIgnoreCase),
                // rejoin in case the name itself contained a tab
                Name = string.Join("\t", parts.Skip(2))
            });
        }

        /// <summary>
        /// Saves the config whenever an odds rule is added, removed or edited. Attached
        /// once, after loading, so restoring the settings does not rewrite the file.
        /// </summary>
        private void AttachOddsRules()
        {
            OddsRules.CollectionChanged += OnOddsRulesChanged;
            foreach (var rule in OddsRules)
                rule.PropertyChanged += OnOddsRuleChanged;
        }

        private void OnOddsRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (OddsRule rule in e.OldItems)
                    rule.PropertyChanged -= OnOddsRuleChanged;
            if (e.NewItems != null)
                foreach (OddsRule rule in e.NewItems)
                    rule.PropertyChanged += OnOddsRuleChanged;
            Save();
        }

        private void OnOddsRuleChanged(object? sender, PropertyChangedEventArgs e) => Save();

        // zh-CN is the culture of the embedded (neutral) resx, en lives in a satellite assembly
        public CultureInfo GetCulture() =>
            Language == "en" ? new CultureInfo("en") : new CultureInfo("zh-CN");
    }
}
