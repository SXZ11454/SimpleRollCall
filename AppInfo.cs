using System.Reflection;
using Res = SimpleRollCall.Properties.Resources;

namespace SimpleRollCall;

/// <summary>
/// Facts about the running build that must never be hard coded into a resource,
/// because they would drift away from the project file.
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// The version the assembly was built with. The SDK fills it from the csproj
    /// (&lt;Version&gt;), so bumping the version there is all it takes - the settings
    /// page follows automatically. Any "+&lt;source revision&gt;" build metadata that
    /// the toolchain appends is cut off again.
    /// </summary>
    public static string Version
    {
        get
        {
            var assembly = typeof(AppInfo).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            var version = !string.IsNullOrEmpty(informational)
                ? informational
                : assembly.GetName().Version?.ToString() ?? "";

            var buildMetadata = version.IndexOf('+');
            return buildMetadata >= 0 ? version[..buildMetadata] : version;
        }
    }

    /// <summary>
    /// The localized version line shown on the settings page, e.g. <c>版本 1.0.0</c>:
    /// the translated word from the resx plus <see cref="Version"/> from the build.
    /// </summary>
    public static string DisplayVersion
    {
        get
        {
            var label = Res.Version ?? "";

            // Keep the translated word only; a number still sitting in the resource
            // (older resx versions carried one) is dropped so it cannot go stale.
            var firstDigit = label.IndexOfAny("0123456789".ToCharArray());
            var prefix = firstDigit >= 0 ? label[..firstDigit] : label;

            if (prefix.Length > 0 && !char.IsWhiteSpace(prefix[^1]))
                prefix += " ";

            return prefix + Version;
        }
    }
}
