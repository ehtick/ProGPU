using System.Runtime.InteropServices;
using System.Text;

namespace ProGPU.DirectX;

public sealed record ProGpuDirectXNativeImportParameter(
    string Name,
    string TypeName,
    bool IsIn,
    bool IsOut,
    bool IsByRef,
    bool IsOptional)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? TypeName : $"{TypeName} {Name}";
}

public sealed record ProGpuDirectXNativeImport(
    string AssemblyName,
    string TypeName,
    string MethodName,
    string ModuleName,
    string EntryPoint,
    string ReturnTypeName,
    IReadOnlyList<ProGpuDirectXNativeImportParameter> Parameters,
    CallingConvention CallingConvention,
    CharSet CharSet,
    bool SetLastError,
    bool ExactSpelling)
{
    public string ManagedSignature =>
        $"{ReturnTypeName} {TypeName}.{MethodName}({string.Join(", ", Parameters.Select(static parameter => parameter.DisplayName))})";
}

public sealed record ProGpuDirectXNativeModuleHint(
    string AssemblyName,
    string ModuleName,
    string Source);

public sealed record ProGpuDirectXNativeDependencyReport(
    IReadOnlyList<ProGpuDirectXNativeImport> Imports,
    IReadOnlyList<ProGpuDirectXNativeModuleHint> ModuleHints,
    IReadOnlyList<string> NativeModules)
{
    public bool RequiresNativeRuntime => NativeModules.Count > 0;

    public bool RequiresModule(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return false;
        }

        return NativeModules.Contains(moduleName.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public string DescribeModules()
    {
        return NativeModules.Count == 0 ? "none" : string.Join(", ", NativeModules);
    }
}

public static class ProGpuDirectXNativeDependencyInspector
{
    public static ProGpuDirectXNativeDependencyReport Inspect(
        IEnumerable<ProGpuDirectXNativeImport> imports,
        IEnumerable<ProGpuDirectXNativeModuleHint>? moduleHints = null)
    {
        return CreateReport(imports, moduleHints);
    }

    public static ProGpuDirectXNativeDependencyReport CreateReport(
        IEnumerable<ProGpuDirectXNativeImport> imports,
        IEnumerable<ProGpuDirectXNativeModuleHint>? moduleHints = null)
    {
        ArgumentNullException.ThrowIfNull(imports);

        var importList = new List<ProGpuDirectXNativeImport>();
        foreach (var import in imports)
        {
            if (import is null)
            {
                throw new ArgumentException("Native dependency reports cannot include a null import.", nameof(imports));
            }

            var moduleName = NormalizeModuleName(import.ModuleName);
            if (moduleName.Length == 0)
            {
                continue;
            }

            importList.Add(import with { ModuleName = moduleName });
        }

        var hintList = new List<ProGpuDirectXNativeModuleHint>();
        foreach (var hint in moduleHints ?? Array.Empty<ProGpuDirectXNativeModuleHint>())
        {
            if (hint is null)
            {
                throw new ArgumentException("Native dependency reports cannot include a null module hint.", nameof(moduleHints));
            }

            var moduleName = NormalizeModuleHint(hint.ModuleName);
            if (moduleName.Length != 0)
            {
                hintList.Add(hint with { ModuleName = moduleName });
            }
        }

        var orderedImports = importList
            .OrderBy(static import => import.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static import => import.EntryPoint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static import => import.TypeName, StringComparer.Ordinal)
            .ThenBy(static import => import.MethodName, StringComparer.Ordinal)
            .ToArray();

        var orderedModuleHints = hintList
            .DistinctBy(static hint => (hint.AssemblyName, hint.ModuleName, hint.Source))
            .OrderBy(static hint => hint.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static hint => hint.AssemblyName, StringComparer.Ordinal)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToArray();

        var nativeModules = orderedImports
            .Select(static import => import.ModuleName)
            .Concat(orderedModuleHints.Select(static hint => hint.ModuleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static moduleName => moduleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProGpuDirectXNativeDependencyReport(orderedImports, orderedModuleHints, nativeModules);
    }

    public static IReadOnlyList<ProGpuDirectXNativeModuleHint> CreateModuleHintsFromText(
        string assemblyName,
        string text,
        string source = "Text")
    {
        ArgumentNullException.ThrowIfNull(text);

        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ExtractModuleNamesFromText(text, modules);
        return CreateModuleHints(assemblyName, source, modules);
    }

    public static IReadOnlyList<ProGpuDirectXNativeModuleHint> CreateModuleHintsFromBytes(
        string assemblyName,
        ReadOnlySpan<byte> bytes,
        string source = "Bytes")
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ExtractAsciiModuleNames(bytes, modules);
        ExtractUtf16LeModuleNames(bytes, modules, startOffset: 0);
        ExtractUtf16LeModuleNames(bytes, modules, startOffset: 1);
        return CreateModuleHints(assemblyName, source, modules);
    }

    public static IReadOnlyList<ProGpuDirectXNativeModuleHint> CreateModuleHintsFromAssemblyImages(
        IEnumerable<Type> anchorTypes,
        string source = "AssemblyImage")
    {
        ArgumentNullException.ThrowIfNull(anchorTypes);

        var hints = new List<ProGpuDirectXNativeModuleHint>();
        var seenAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchorType in anchorTypes)
        {
            if (anchorType is null)
            {
                throw new ArgumentException("Assembly image native dependency hints cannot include a null anchor type.", nameof(anchorTypes));
            }

            var assembly = anchorType.Assembly;
            var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? string.Empty;
            if (!seenAssemblies.Add(assembly.FullName ?? assemblyName))
            {
                continue;
            }

            var location = assembly.Location;
            if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
            {
                continue;
            }

            try
            {
                hints.AddRange(CreateModuleHintsFromBytes(
                    assemblyName,
                    File.ReadAllBytes(location),
                    source));
            }
            catch (Exception ex) when (IsAssemblyImageReadFailure(ex))
            {
                hints.Add(new ProGpuDirectXNativeModuleHint(
                    assemblyName,
                    $"{assemblyName}.dll",
                    $"{source}ReadFailed:{DescribeAssemblyImageReadFailure(ex)}"));
            }
        }

        return hints
            .DistinctBy(static hint => (hint.AssemblyName, hint.ModuleName, hint.Source))
            .OrderBy(static hint => hint.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static hint => hint.AssemblyName, StringComparer.Ordinal)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ExtractAsciiModuleNames(ReadOnlySpan<byte> bytes, HashSet<string> modules)
    {
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            if (value >= 32 && value <= 126)
            {
                AppendModuleHintChar(builder, (char)value, modules);
            }
            else
            {
                FlushModuleHint(builder, modules);
            }
        }

        FlushModuleHint(builder, modules);
    }

    private static void ExtractModuleNamesFromText(string text, HashSet<string> modules)
    {
        var builder = new StringBuilder();
        foreach (var value in text)
        {
            if (value >= 32 && value <= 126)
            {
                AppendModuleHintChar(builder, value, modules);
            }
            else
            {
                FlushModuleHint(builder, modules);
            }
        }

        FlushModuleHint(builder, modules);
    }

    private static void ExtractUtf16LeModuleNames(ReadOnlySpan<byte> bytes, HashSet<string> modules, int startOffset)
    {
        var builder = new StringBuilder();
        for (var i = startOffset; i + 1 < bytes.Length; i += 2)
        {
            var value = bytes[i];
            var high = bytes[i + 1];
            if (high == 0 && value >= 32 && value <= 126)
            {
                AppendModuleHintChar(builder, (char)value, modules);
            }
            else
            {
                FlushModuleHint(builder, modules);
            }
        }

        FlushModuleHint(builder, modules);
    }

    private static void AppendModuleHintChar(StringBuilder builder, char value, HashSet<string> modules)
    {
        if (char.IsWhiteSpace(value) || value is '"' or '\'' or '<' or '>' or '(' or ')' or '[' or ']' or '{' or '}' or ',' or ';')
        {
            FlushModuleHint(builder, modules);
            return;
        }

        builder.Append(value);
        if (builder.Length > 512)
        {
            FlushModuleHint(builder, modules);
        }
    }

    private static void FlushModuleHint(StringBuilder builder, HashSet<string> modules)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var moduleName = NormalizeModuleHint(builder.ToString());
        if (moduleName.Length != 0)
        {
            modules.Add(moduleName);
        }

        builder.Clear();
    }

    private static string NormalizeModuleHint(string value)
    {
        var normalized = value.Trim().TrimEnd('.', ':', '!', '?');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized[0] == '.' || normalized.Contains('*', StringComparison.Ordinal) || normalized.Contains('?', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        normalized = normalized.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        if (normalized.Length == 0 || normalized[0] == '.' || normalized.Contains('*', StringComparison.Ordinal) || normalized.Contains('?', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return IsNativeModuleName(normalized) ? normalized : string.Empty;
    }

    private static bool IsNativeModuleName(string value)
    {
        return value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssemblyImageReadFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
    }

    private static string DescribeAssemblyImageReadFailure(Exception exception)
    {
        return exception switch
        {
            IOException => "IO",
            UnauthorizedAccessException => "UnauthorizedAccess",
            NotSupportedException => "NotSupported",
            ArgumentException => "InvalidArgument",
            _ => "Unavailable"
        };
    }

    private static string NormalizeModuleName(string? moduleName)
    {
        return string.IsNullOrWhiteSpace(moduleName) ? string.Empty : moduleName.Trim();
    }

    private static IReadOnlyList<ProGpuDirectXNativeModuleHint> CreateModuleHints(
        string assemblyName,
        string source,
        IEnumerable<string> modules)
    {
        return modules
            .Select(NormalizeModuleHint)
            .Where(static moduleName => moduleName.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static moduleName => moduleName, StringComparer.OrdinalIgnoreCase)
            .Select(moduleName => new ProGpuDirectXNativeModuleHint(
                string.IsNullOrWhiteSpace(assemblyName) ? string.Empty : assemblyName.Trim(),
                moduleName,
                string.IsNullOrWhiteSpace(source) ? "Explicit" : source.Trim()))
            .ToArray();
    }
}
