using System.Runtime.InteropServices;
using System.Text;

namespace ProGPU.DirectX;

public sealed record ProGpuDirectXNativeFacadeSourceOptions(
    string NamespaceName = "ProGPU.DirectX.NativeFacade",
    string ClassName = "ProGpuDirectXNativeFacadeExports")
{
    public static ProGpuDirectXNativeFacadeSourceOptions Default { get; } = new();
}

public sealed record ProGpuDirectXNativeFacadeExportStub(
    string ModuleName,
    string EntryPoint,
    string MethodName,
    ProGpuDirectXNativeModuleKind Kind,
    ProGpuDirectXNativeCompatibilityAction Action,
    CallingConvention CallingConvention,
    string? ManagedSignature,
    bool IsSupported,
    string? UnsupportedReason);

public sealed record ProGpuDirectXNativeFacadeSource(
    string SourceText,
    IReadOnlyList<ProGpuDirectXNativeFacadeExportStub> Exports)
{
    public IReadOnlyList<ProGpuDirectXNativeFacadeExportStub> SupportedExports =>
        Exports.Where(static export => export.IsSupported).ToArray();

    public IReadOnlyList<ProGpuDirectXNativeFacadeExportStub> UnsupportedExports =>
        Exports.Where(static export => !export.IsSupported).ToArray();

    public string DescribeSupport() =>
        $"{SupportedExports.Count} supported native facade exports, {UnsupportedExports.Count} unsupported native facade exports";
}

public sealed record ProGpuDirectXNativeFacadeProjectOptions(
    string ProjectName = "ProGPU.DirectX.NativeFacade",
    string TargetFramework = "net10.0",
    string? RuntimeIdentifier = null,
    bool InvariantGlobalization = true)
{
    public static ProGpuDirectXNativeFacadeProjectOptions Default { get; } = new();
}

public sealed record ProGpuDirectXNativeFacadeProject(
    string ProjectFileName,
    string ProjectFileText,
    string SourceFileName,
    string SourceText,
    string ReadmeFileName,
    string ReadmeText,
    ProGpuDirectXNativeFacadeSource Source)
{
    public string DescribeSupport() => Source.DescribeSupport();

    public void WriteToDirectory(string directoryPath, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("A native facade project directory path is required.", nameof(directoryPath));
        }

        Directory.CreateDirectory(directoryPath);
        WriteFile(Path.Combine(directoryPath, ProjectFileName), ProjectFileText, overwrite);
        WriteFile(Path.Combine(directoryPath, SourceFileName), SourceText, overwrite);
        WriteFile(Path.Combine(directoryPath, ReadmeFileName), ReadmeText, overwrite);
    }

    private static void WriteFile(string path, string text, bool overwrite)
    {
        if (!overwrite && File.Exists(path))
        {
            throw new IOException($"The native facade project file already exists: {path}");
        }

        File.WriteAllText(path, text);
    }
}

public static class ProGpuDirectXNativeFacadeSourceEmitter
{
    public static ProGpuDirectXNativeFacadeSource Emit(
        ProGpuDirectXNativeAbiPlan plan,
        ProGpuDirectXNativeFacadeSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= ProGpuDirectXNativeFacadeSourceOptions.Default;

        var methodNames = new HashSet<string>(StringComparer.Ordinal);
        var stubs = plan.ActionableExports
            .Select(export => CreateStub(export, methodNames))
            .ToArray();

        var source = BuildSource(options, stubs);
        return new ProGpuDirectXNativeFacadeSource(source, stubs);
    }

    private static ProGpuDirectXNativeFacadeExportStub CreateStub(
        ProGpuDirectXNativeAbiExportRequirement export,
        HashSet<string> methodNames)
    {
        var methodName = CreateUniqueMethodName(export.ModuleName, export.EntryPoint, methodNames);
        var signatures = export.ManagedSignatures;
        if (signatures.Count != 1)
        {
            return new ProGpuDirectXNativeFacadeExportStub(
                export.ModuleName,
                export.EntryPoint,
                methodName,
                export.Kind,
                export.Action,
                export.CallingConvention,
                ManagedSignature: null,
                IsSupported: false,
                UnsupportedReason: $"Expected one managed signature, found {signatures.Count}.");
        }

        var import = export.Imports[0];
        if (!TryMapReturn(import.ReturnTypeName, out _, out _, out var returnFailure))
        {
            return new ProGpuDirectXNativeFacadeExportStub(
                export.ModuleName,
                export.EntryPoint,
                methodName,
                export.Kind,
                export.Action,
                export.CallingConvention,
                signatures[0],
                IsSupported: false,
                UnsupportedReason: returnFailure);
        }

        foreach (var parameter in import.Parameters)
        {
            if (!TryMapParameter(parameter, out _, out var parameterFailure))
            {
                return new ProGpuDirectXNativeFacadeExportStub(
                    export.ModuleName,
                    export.EntryPoint,
                    methodName,
                    export.Kind,
                    export.Action,
                    export.CallingConvention,
                    signatures[0],
                    IsSupported: false,
                    UnsupportedReason: parameterFailure);
            }
        }

        return new ProGpuDirectXNativeFacadeExportStub(
            export.ModuleName,
            export.EntryPoint,
            methodName,
            export.Kind,
            export.Action,
            export.CallingConvention,
            signatures[0],
            IsSupported: true,
            UnsupportedReason: null);
    }

    private static string BuildSource(
        ProGpuDirectXNativeFacadeSourceOptions options,
        IReadOnlyList<ProGpuDirectXNativeFacadeExportStub> stubs)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine("using System.Runtime.InteropServices;");
        builder.AppendLine();
        builder.Append("namespace ").AppendLine(SanitizeNamespace(options.NamespaceName)).AppendLine("{");
        builder.Append("    public static unsafe partial class ").AppendLine(SanitizeIdentifier(options.ClassName)).AppendLine("    {");

        foreach (var stub in stubs)
        {
            if (stub.IsSupported)
            {
                AppendExportMethod(builder, stub);
            }
            else
            {
                builder
                    .Append("        // Unsupported native facade export ")
                    .Append(stub.ModuleName)
                    .Append('!')
                    .Append(stub.EntryPoint)
                    .Append(": ")
                    .AppendLine(stub.UnsupportedReason ?? "unsupported signature.");
            }
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendExportMethod(StringBuilder builder, ProGpuDirectXNativeFacadeExportStub stub)
    {
        // The generated source is a conservative NativeAOT scaffold. It returns default values so the
        // first native facade project can export the full symbol set before individual exports are
        // routed to ProGPU.DirectX, host OS abstractions, or fail-fast diagnostics.
        builder.AppendLine();
        builder.Append("        // ").Append(stub.ModuleName).Append('!').AppendLine(stub.EntryPoint);
        if (!string.IsNullOrWhiteSpace(stub.ManagedSignature))
        {
            builder.Append("        // ").AppendLine(stub.ManagedSignature);
        }

        builder
            .Append("        [UnmanagedCallersOnly(EntryPoint = \"")
            .Append(EscapeString(stub.EntryPoint))
            .Append('"');
        var callConv = GetCallConvType(stub.CallingConvention);
        if (!string.IsNullOrEmpty(callConv))
        {
            builder.Append(", CallConvs = new[] { typeof(").Append(callConv).Append(") }");
        }

        builder.AppendLine(")]");

        var signature = ParseManagedSignature(stub.ManagedSignature);
        builder
            .Append("        public static ")
            .Append(signature.ReturnType)
            .Append(' ')
            .Append(stub.MethodName)
            .Append('(')
            .Append(string.Join(", ", signature.Parameters))
            .AppendLine(")");
        builder.AppendLine("        {");
        if (signature.ReturnType == "void")
        {
            builder.AppendLine("            return;");
        }
        else
        {
            builder.Append("            return ").Append(signature.DefaultReturnValue).AppendLine(";");
        }

        builder.AppendLine("        }");
    }

    private static string? GetCallConvType(CallingConvention callingConvention)
    {
        return callingConvention switch
        {
            CallingConvention.Cdecl => "CallConvCdecl",
            CallingConvention.FastCall => "CallConvFastcall",
            CallingConvention.StdCall => "CallConvStdcall",
            CallingConvention.ThisCall => "CallConvThiscall",
            CallingConvention.Winapi => "CallConvStdcall",
            _ => null
        };
    }

    private static (string ReturnType, string DefaultReturnValue, IReadOnlyList<string> Parameters) ParseManagedSignature(
        string? managedSignature)
    {
        if (string.IsNullOrWhiteSpace(managedSignature))
        {
            return ("int", "0", []);
        }

        var openParen = managedSignature.IndexOf('(');
        var closeParen = managedSignature.LastIndexOf(')');
        var beforeParen = openParen <= 0 ? managedSignature : managedSignature[..openParen];
        var firstSpace = beforeParen.IndexOf(' ');
        var returnTypeName = firstSpace <= 0 ? "System.Int32" : beforeParen[..firstSpace];
        _ = TryMapReturn(returnTypeName, out var returnType, out var defaultReturn, out _);

        if (openParen < 0 || closeParen < openParen)
        {
            return (returnType, defaultReturn, []);
        }

        var parameterText = managedSignature[(openParen + 1)..closeParen];
        if (string.IsNullOrWhiteSpace(parameterText))
        {
            return (returnType, defaultReturn, []);
        }

        var parameters = parameterText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select((parameter, index) =>
            {
                var separator = parameter.LastIndexOf(' ');
                var typeName = separator <= 0 ? parameter : parameter[..separator];
                var name = separator <= 0 ? $"arg{index}" : parameter[(separator + 1)..];
                var nativeType = TryMapParameterTypeName(typeName, out var mappedType)
                    ? mappedType
                    : "nint";
                return $"{nativeType} {SanitizeIdentifier(name)}";
            })
            .ToArray();

        return (returnType, defaultReturn, parameters);
    }

    private static bool TryMapReturn(
        string typeName,
        out string nativeTypeName,
        out string defaultReturnValue,
        out string? failure)
    {
        if (typeName == "System.Void")
        {
            nativeTypeName = "void";
            defaultReturnValue = string.Empty;
            failure = null;
            return true;
        }

        if (TryMapParameterTypeName(typeName, out nativeTypeName))
        {
            defaultReturnValue = nativeTypeName switch
            {
                "float" => "0f",
                "double" => "0d",
                _ => "0"
            };
            failure = null;
            return true;
        }

        defaultReturnValue = string.Empty;
        failure = $"Unsupported native facade return type '{typeName}'.";
        return false;
    }

    private static bool TryMapParameter(ProGpuDirectXNativeImportParameter parameter, out string nativeTypeName, out string? failure)
    {
        if (TryMapParameterTypeName(parameter.TypeName, out nativeTypeName))
        {
            failure = null;
            return true;
        }

        failure = $"Unsupported native facade parameter type '{parameter.TypeName}' for '{parameter.Name}'.";
        return false;
    }

    private static bool TryMapParameterTypeName(string typeName, out string nativeTypeName)
    {
        nativeTypeName = typeName switch
        {
            "System.Boolean" => "int",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Char" => "ushort",
            "System.IntPtr" => "nint",
            "System.UIntPtr" => "nuint",
            "System.String" => "nint",
            _ when typeName.EndsWith("&", StringComparison.Ordinal) => "nint",
            _ when typeName.EndsWith("*", StringComparison.Ordinal) => "nint",
            _ when typeName.EndsWith("[]", StringComparison.Ordinal) => "nint",
            _ => string.Empty
        };

        return nativeTypeName.Length != 0;
    }

    private static string CreateUniqueMethodName(string moduleName, string entryPoint, HashSet<string> methodNames)
    {
        var baseName = $"{SanitizeIdentifier(moduleName)}_{SanitizeIdentifier(entryPoint)}";
        var methodName = baseName;
        var suffix = 1;
        while (!methodNames.Add(methodName))
        {
            methodName = $"{baseName}_{suffix++}";
        }

        return methodName;
    }

    private static string SanitizeNamespace(string value)
    {
        var parts = value
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeIdentifier)
            .Where(static part => part.Length != 0)
            .ToArray();
        return parts.Length == 0 ? "ProGPU.DirectX.NativeFacade" : string.Join(".", parts);
    }

    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        if (builder.Length == 0)
        {
            return "_";
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private static string EscapeString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

public static class ProGpuDirectXNativeFacadeProjectEmitter
{
    public static ProGpuDirectXNativeFacadeProject Emit(
        ProGpuDirectXNativeAbiPlan plan,
        ProGpuDirectXNativeFacadeProjectOptions? projectOptions = null,
        ProGpuDirectXNativeFacadeSourceOptions? sourceOptions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        projectOptions ??= ProGpuDirectXNativeFacadeProjectOptions.Default;
        sourceOptions ??= ProGpuDirectXNativeFacadeSourceOptions.Default;

        var source = ProGpuDirectXNativeFacadeSourceEmitter.Emit(plan, sourceOptions);
        var projectName = SanitizeProjectName(projectOptions.ProjectName);
        var sourceFileName = $"{SanitizeFileStem(sourceOptions.ClassName)}.g.cs";
        return new ProGpuDirectXNativeFacadeProject(
            $"{projectName}.csproj",
            BuildProjectFile(projectOptions, projectName),
            sourceFileName,
            source.SourceText,
            "README.md",
            BuildReadme(projectName, projectOptions, source),
            source);
    }

    private static string BuildProjectFile(
        ProGpuDirectXNativeFacadeProjectOptions options,
        string projectName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        builder.AppendLine("  <PropertyGroup>");
        builder.Append("    <TargetFramework>").Append(EscapeXml(options.TargetFramework)).AppendLine("</TargetFramework>");
        builder.AppendLine("    <OutputType>Library</OutputType>");
        builder.AppendLine("    <PublishAot>true</PublishAot>");
        builder.AppendLine("    <NativeLib>Shared</NativeLib>");
        builder.AppendLine("    <SelfContained>true</SelfContained>");
        builder.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        builder.AppendLine("    <Nullable>enable</Nullable>");
        builder.Append("    <AssemblyName>").Append(EscapeXml(projectName)).AppendLine("</AssemblyName>");
        if (!string.IsNullOrWhiteSpace(options.RuntimeIdentifier))
        {
            builder.Append("    <RuntimeIdentifier>").Append(EscapeXml(options.RuntimeIdentifier!)).AppendLine("</RuntimeIdentifier>");
        }

        builder.Append("    <InvariantGlobalization>")
            .Append(options.InvariantGlobalization ? "true" : "false")
            .AppendLine("</InvariantGlobalization>");
        builder.AppendLine("  </PropertyGroup>");
        builder.AppendLine("</Project>");
        return builder.ToString();
    }

    private static string BuildReadme(
        string projectName,
        ProGpuDirectXNativeFacadeProjectOptions options,
        ProGpuDirectXNativeFacadeSource source)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(projectName);
        builder.AppendLine();
        builder.AppendLine("This is a generated NativeAOT shared-library scaffold for DirectX/native compatibility exports.");
        builder.AppendLine("The generated exports return conservative default values until each export is replaced with a ProGPU DirectX, host OS, or fail-fast implementation.");
        builder.AppendLine();
        builder.Append("- Target framework: `").Append(options.TargetFramework).AppendLine("`");
        builder.Append("- NativeAOT mode: `").AppendLine("Shared`");
        builder.Append("- Supported exports: `").Append(source.SupportedExports.Count).AppendLine("`");
        builder.Append("- Unsupported exports: `").Append(source.UnsupportedExports.Count).AppendLine("`");
        if (!string.IsNullOrWhiteSpace(options.RuntimeIdentifier))
        {
            builder.Append("- Runtime identifier: `").Append(options.RuntimeIdentifier).AppendLine("`");
        }

        builder.AppendLine();
        builder.AppendLine("Publish with:");
        builder.AppendLine();
        builder.Append("```bash").AppendLine();
        builder.Append("dotnet publish ").Append(projectName).Append(".csproj -c Release");
        if (!string.IsNullOrWhiteSpace(options.RuntimeIdentifier))
        {
            builder.Append(" -r ").Append(options.RuntimeIdentifier);
        }

        builder.AppendLine();
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string SanitizeProjectName(string value)
    {
        var sanitized = SanitizeFileStem(value);
        return sanitized.Length == 0 ? "ProGPU.DirectX.NativeFacade" : sanitized;
    }

    private static string SanitizeFileStem(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NativeFacade";
        }

        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_');
        }

        return builder.ToString().Trim('.');
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
}
