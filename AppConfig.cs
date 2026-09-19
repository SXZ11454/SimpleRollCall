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
                }
            }
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
            File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }

        public CultureInfo GetCulture() =>
            Language == "en" ? new CultureInfo("en") : new CultureInfo("zh");
    }
}
