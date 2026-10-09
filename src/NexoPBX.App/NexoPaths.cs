// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.App;

/// <summary>Where configuration and data live. Resolved once at startup.</summary>
/// <param name="ConfigFile">YAML configuration file (may not exist).</param>
/// <param name="DataDirectory">Directory for the database, keys and runtime files.</param>
public sealed record NexoPaths(string ConfigFile, string DataDirectory)
{
    /// <summary>Environment variable overriding the configuration file location.</summary>
    public const string ConfigEnvVar = "NEXOPBX_CONFIG";

    /// <summary>Environment variable overriding the data directory.</summary>
    public const string DataDirEnvVar = "NEXOPBX_DATA_DIR";

    /// <summary>
    /// Resolves paths. Explicit arguments win over environment variables, which win over defaults.
    /// Service mode uses system locations; console mode keeps everything next to where you run it.
    /// </summary>
    public static NexoPaths Resolve(bool serviceMode, string? configOverride = null, string? dataOverride = null)
    {
        var config = FirstNonEmpty(configOverride, Environment.GetEnvironmentVariable(ConfigEnvVar))
            ?? (serviceMode ? Path.Combine(SystemConfigDirectory(), "nexopbx.yaml") : Path.Combine(Directory.GetCurrentDirectory(), "nexopbx.yaml"));
        var data = FirstNonEmpty(dataOverride, Environment.GetEnvironmentVariable(DataDirEnvVar))
            ?? (serviceMode ? SystemDataDirectory() : Path.Combine(Directory.GetCurrentDirectory(), "data"));
        return new NexoPaths(Path.GetFullPath(config), Path.GetFullPath(data));
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string SystemConfigDirectory() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexoPBX")
        : "/etc/nexopbx";

    private static string SystemDataDirectory() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexoPBX", "data")
        : "/var/lib/nexopbx";
}
