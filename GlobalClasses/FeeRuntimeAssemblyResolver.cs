using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace VIBN_Tools.GlobalClasses;

internal static class FeeRuntimeAssemblyResolver
{
    private static string[] _directories = [];
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        var appDirectory = AppContext.BaseDirectory;
        _directories = new[]
        {
            appDirectory,
            Path.Combine(appDirectory, "FeeRuntime")
        }.Where(Directory.Exists).ToArray();
        AssemblyLoadContext.Default.Resolving += Resolve;
        _registered = true;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName requested)
    {
        if (string.IsNullOrWhiteSpace(requested.Name) ||
            requested.Name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
            !string.IsNullOrEmpty(requested.CultureName))
            return null;

        // Resolving runs only after normal .NET/package resolution has failed.
        // Existing NuGet assets (in particular SixLabors.Fonts) keep priority.
        foreach (var directory in _directories)
        {
            var path = Path.Combine(directory, requested.Name + ".dll");
            if (!File.Exists(path))
                continue;
            AssemblyName candidate;
            try
            {
                candidate = AssemblyName.GetAssemblyName(path);
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                continue;
            }
            if (!string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase) ||
                !(candidate.GetPublicKeyToken() ?? []).SequenceEqual(requested.GetPublicKeyToken() ?? []) ||
                (requested.Version is not null && candidate.Version is not null && candidate.Version < requested.Version))
                continue;
            return context.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        return null;
    }
}
