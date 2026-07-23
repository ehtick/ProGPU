using System;

namespace ProGPU.Xaml.Roslyn;

[Flags]
public enum RoslynXamlExtensionCapabilities
{
    None = 0,
    MarkupExtensionExpression = 1 << 0,
    BoundDocumentValidation = 1 << 1
}

public enum RoslynXamlExtensionConflictPolicy
{
    Diagnose,
    CoalesceEquivalent
}

public interface IRoslynXamlExtension
{
    string Id { get; }
    int ContractVersion { get; }
    int Version { get; }
    int Priority { get; }
    RoslynXamlExtensionCapabilities Capabilities { get; }
    RoslynXamlExtensionConflictPolicy ConflictPolicy { get; }
}
