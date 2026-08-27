using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace VIBN_Tools.Settings;

/// <summary>Immutable result of the local FEE version discovery.</summary>
public sealed record FeeVersionInfo(
    string UsedSdkVersion,
    string InstalledFeeVersion,
    bool HasVersionMismatch,
    string StatusMessage);

public interface IFeeVersionInfoProvider
{
    FeeVersionInfo Read();
}

/// <summary>
/// Reads the SDK selected during compilation and the newest locally installed
/// fe.screen-sim version. Discovery is intentionally best-effort: missing or
/// inaccessible registry keys must never prevent Project Settings from loading.
/// </summary>
public sealed class FeeVersionInfoProvider : IFeeVersionInfoProvider
{
    private const string UnknownVersion = "Nicht erkannt";
    private static readonly Regex NumericVersionPattern =
        new(@"(?<!\d)(\d+(?:\.\d+){1,3})(?!\d)", RegexOptions.Compiled);

    public FeeVersionInfo Read()
    {
        var usedSdkVersion = DetectUsedSdkVersion();
        var installedFeeVersion = DetectInstalledFeeVersion();
        var mismatch = TryParseVersion(usedSdkVersion, out var used) &&
                       TryParseVersion(installedFeeVersion, out var installed) &&
                       used != installed;
        var status = mismatch
            ? $"Versionsabweichung: SDK {usedSdkVersion}, lokal installiert {installedFeeVersion}."
            : "SDK und lokale FEE-Version stimmen überein.";

        if (usedSdkVersion == UnknownVersion || installedFeeVersion == UnknownVersion)
            status = "Ein vollständiger Versionsvergleich ist nicht möglich.";

        return new FeeVersionInfo(usedSdkVersion, installedFeeVersion, mismatch, status);
    }

    private static string DetectUsedSdkVersion()
    {
        try
        {
            // Prefer the actual runtime binary. The build metadata remains a
            // fallback for diagnostic builds in which dependencies are not yet
            // copied next to the executable.
            var sdkPath = Path.Combine(AppContext.BaseDirectory, "FS.SDK.dll");
            if (File.Exists(sdkPath))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(sdkPath);
                if (TryParseVersion(versionInfo.ProductVersion, out var productVersion))
                    return Format(productVersion);
                if (TryParseVersion(versionInfo.FileVersion, out var fileVersion))
                    return Format(fileVersion);
            }

            var metadataVersion = typeof(FeeVersionInfoProvider).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Key, "FeeSdkVersion", StringComparison.Ordinal))
                ?.Value;
            if (TryParseVersion(metadataVersion, out var parsedMetadata))
                return Format(parsedMetadata);
        }
        catch
        {
            // Version display is diagnostic only and must never block startup.
        }

        return UnknownVersion;
    }

    private static string DetectInstalledFeeVersion()
    {
        var versions = new List<Version>();
        AddDirectoryVersions(versions);
        AddRegistryVersions(versions);
        return versions.Count == 0 ? UnknownVersion : Format(versions.Max()!);
    }

    private static void AddDirectoryVersions(ICollection<Version> versions)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var programFiles in roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var installRoot = Path.Combine(programFiles, "fe.screen-sim V5");
                if (!Directory.Exists(installRoot))
                    continue;

                foreach (var directory in Directory.EnumerateDirectories(installRoot))
                {
                    if (TryParseVersion(Path.GetFileName(directory), out var version))
                        versions.Add(version);
                }
            }
            catch
            {
                // Continue with registry discovery when a folder is inaccessible.
            }
        }
    }

    private static void AddRegistryVersions(ICollection<Version> versions)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null)
                        continue;

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        using var product = uninstall.OpenSubKey(subKeyName);
                        var displayName = product?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName) ||
                            !displayName.Contains("screen-sim", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (TryParseVersion(product?.GetValue("DisplayVersion") as string, out var version))
                            versions.Add(version);
                    }
                }
                catch
                {
                    // Registry access is optional and can be restricted by policy.
                }
            }
        }
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = NumericVersionPattern.Match(value);
        return match.Success && Version.TryParse(match.Groups[1].Value, out version);
    }

    private static string Format(Version version) => $"V{version}";
}
