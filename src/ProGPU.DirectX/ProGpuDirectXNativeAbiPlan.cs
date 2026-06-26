using System.Runtime.InteropServices;

namespace ProGPU.DirectX;

public sealed record ProGpuDirectXNativeAbiExportRequirement(
    string ModuleName,
    string EntryPoint,
    ProGpuDirectXNativeModuleKind Kind,
    ProGpuDirectXNativeCompatibilityAction Action,
    string Reason,
    CallingConvention CallingConvention,
    CharSet CharSet,
    bool SetLastError,
    bool ExactSpelling,
    IReadOnlyList<ProGpuDirectXNativeImport> Imports)
{
    public string DisplayName => $"{ModuleName}!{EntryPoint}";

    public IReadOnlyList<string> ManagedSignatures =>
        Imports
            .Select(static import => import.ManagedSignature)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}

public sealed record ProGpuDirectXNativeAbiModuleRequirement(
    string ModuleName,
    ProGpuDirectXNativeModuleKind Kind,
    ProGpuDirectXNativeCompatibilityAction Action,
    string Reason,
    IReadOnlyList<ProGpuDirectXNativeAbiExportRequirement> Exports,
    IReadOnlyList<ProGpuDirectXNativeModuleHint> ModuleHints)
{
    public bool HasKnownExports => Exports.Count != 0;

    public bool HasOnlyDynamicHints => Exports.Count == 0 && ModuleHints.Count != 0;
}

public sealed record ProGpuDirectXNativeAbiPlan(
    IReadOnlyList<ProGpuDirectXNativeAbiModuleRequirement> Modules)
{
    public IReadOnlyList<ProGpuDirectXNativeAbiExportRequirement> ActionableExports =>
        Modules
            .SelectMany(static module => module.Exports)
            .Where(static export => export.Action != ProGpuDirectXNativeCompatibilityAction.ManagedAssemblyReferenceOnly)
            .ToArray();

    public IReadOnlyList<ProGpuDirectXNativeAbiModuleRequirement> DynamicModuleHints =>
        Modules
            .Where(static module => module.HasOnlyDynamicHints
                && module.Action != ProGpuDirectXNativeCompatibilityAction.ManagedAssemblyReferenceOnly)
            .ToArray();

    public string DescribeActionableExports(int maxExportsPerModule = 24)
    {
        List<string> parts = [];
        foreach (var module in Modules)
        {
            if (module.Action == ProGpuDirectXNativeCompatibilityAction.ManagedAssemblyReferenceOnly)
            {
                continue;
            }

            if (module.Exports.Count != 0)
            {
                var entryPoints = module.Exports.Select(static export => export.EntryPoint).ToArray();
                var selectedEntryPoints = maxExportsPerModule <= 0
                    ? entryPoints
                    : entryPoints.Take(maxExportsPerModule).ToArray();
                var suffix = maxExportsPerModule > 0 && entryPoints.Length > maxExportsPerModule
                    ? $", ... +{entryPoints.Length - maxExportsPerModule} more"
                    : string.Empty;
                parts.Add($"{module.ModuleName}: {string.Join(", ", selectedEntryPoints)}{suffix}");
            }
            else if (module.ModuleHints.Count != 0)
            {
                parts.Add($"{module.ModuleName}: dynamic module hint");
            }
        }

        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }
}

public static class ProGpuDirectXNativeAbiPlanner
{
    public static ProGpuDirectXNativeAbiPlan Create(ProGpuDirectXNativeDependencyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var importsByModule = new Dictionary<string, List<ProGpuDirectXNativeImport>>(StringComparer.OrdinalIgnoreCase);
        foreach (var import in report.Imports)
        {
            var moduleName = NormalizeModuleName(import.ModuleName);
            if (!importsByModule.TryGetValue(moduleName, out var imports))
            {
                imports = [];
                importsByModule[moduleName] = imports;
            }

            imports.Add(import);
        }

        var hintsByModule = new Dictionary<string, List<ProGpuDirectXNativeModuleHint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in report.ModuleHints)
        {
            var moduleName = NormalizeModuleName(hint.ModuleName);
            if (!hintsByModule.TryGetValue(moduleName, out var hints))
            {
                hints = [];
                hintsByModule[moduleName] = hints;
            }

            hints.Add(hint);
        }

        var modules = report.NativeModules
            .Select(moduleName => CreateModuleRequirement(
                moduleName,
                importsByModule.TryGetValue(NormalizeModuleName(moduleName), out var imports) ? imports : [],
                hintsByModule.TryGetValue(NormalizeModuleName(moduleName), out var hints) ? hints : []))
            .OrderBy(static module => module.Action)
            .ThenBy(static module => module.Kind)
            .ThenBy(static module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProGpuDirectXNativeAbiPlan(modules);
    }

    private static ProGpuDirectXNativeAbiModuleRequirement CreateModuleRequirement(
        string moduleName,
        IReadOnlyList<ProGpuDirectXNativeImport> imports,
        IReadOnlyList<ProGpuDirectXNativeModuleHint> hints)
    {
        var module = ProGpuDirectXNativeCompatibilityPlanner.Classify(moduleName);
        var exports = imports
            .GroupBy(static import => new ExportGroupKey(
                import.EntryPoint,
                import.CallingConvention,
                import.CharSet,
                import.SetLastError,
                import.ExactSpelling))
            .Select(group => new ProGpuDirectXNativeAbiExportRequirement(
                module.ModuleName,
                group.Key.EntryPoint,
                module.Kind,
                module.Action,
                module.Reason,
                group.Key.CallingConvention,
                group.Key.CharSet,
                group.Key.SetLastError,
                group.Key.ExactSpelling,
                group
                    .OrderBy(static import => import.AssemblyName, StringComparer.Ordinal)
                    .ThenBy(static import => import.TypeName, StringComparer.Ordinal)
                    .ThenBy(static import => import.MethodName, StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(static export => export.EntryPoint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static export => export.CallingConvention)
            .ThenBy(static export => export.CharSet)
            .ToArray();

        var orderedHints = hints
            .OrderBy(static hint => hint.AssemblyName, StringComparer.Ordinal)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToArray();

        return new ProGpuDirectXNativeAbiModuleRequirement(
            module.ModuleName,
            module.Kind,
            module.Action,
            module.Reason,
            exports,
            orderedHints);
    }

    private static string NormalizeModuleName(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return string.Empty;
        }

        var normalized = moduleName.Trim().Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
    }

    private readonly record struct ExportGroupKey(
        string EntryPoint,
        CallingConvention CallingConvention,
        CharSet CharSet,
        bool SetLastError,
        bool ExactSpelling);
}
