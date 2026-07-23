using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Parsing;

[Flags]
public enum XamlMarkupSyntaxContexts
{
    None = 0,
    Standalone = 1 << 0,
    AttributeValue = 1 << 1,
    NestedValue = 1 << 2,
    All = Standalone | AttributeValue | NestedValue
}

public enum XamlMarkupSyntaxAssociativity
{
    None,
    Left,
    Right
}

public enum XamlMarkupSyntaxConflictPolicy
{
    Diagnose,
    CoalesceEquivalent
}

[Flags]
public enum XamlMarkupSyntaxCapabilities
{
    None = 0,
    Tokenize = 1 << 0,
    Parse = 1 << 1,
    Format = 1 << 2,
    CanonicalProjection = 1 << 3
}

/// <summary>
/// Bounded context supplied to a custom markup syntax parser. Plugins project their syntax
/// into the standard immutable markup-extension model so schema binding and lowering remain
/// shared by every framework.
/// </summary>
public sealed class XamlMarkupSyntaxPluginContext
{
    internal XamlMarkupSyntaxPluginContext(
        SourceText source,
        TextSpan span,
        string path,
        XamlMarkupParseOptions options,
        CancellationToken cancellationToken)
    {
        Source = source;
        Span = span;
        Path = path;
        Options = options;
        CancellationToken = cancellationToken;
    }

    public SourceText Source { get; }
    public TextSpan Span { get; }
    public string Path { get; }
    public XamlMarkupParseOptions Options { get; }
    public CancellationToken CancellationToken { get; }
}

public sealed class XamlMarkupSyntaxPluginResult
{
    private XamlMarkupSyntaxPluginResult(
        bool isRecognized,
        XamlMarkupExtension? extension,
        string? errorMessage,
        TextSpan errorSpan)
    {
        IsRecognized = isRecognized;
        Extension = extension;
        ErrorMessage = errorMessage;
        ErrorSpan = errorSpan;
    }

    public static XamlMarkupSyntaxPluginResult NotRecognized { get; } =
        new XamlMarkupSyntaxPluginResult(false, null, null, default);

    public bool IsRecognized { get; }
    public XamlMarkupExtension? Extension { get; }
    public string? ErrorMessage { get; }
    public TextSpan ErrorSpan { get; }
    public bool IsSuccess => IsRecognized && Extension != null && ErrorMessage == null;

    public static XamlMarkupSyntaxPluginResult Success(XamlMarkupExtension extension)
    {
        if (extension == null) throw new ArgumentNullException(nameof(extension));
        return new XamlMarkupSyntaxPluginResult(true, extension, null, default);
    }

    public static XamlMarkupSyntaxPluginResult Failure(string message, TextSpan errorSpan = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A custom syntax failure requires a message.", nameof(message));
        return new XamlMarkupSyntaxPluginResult(true, null, message, errorSpan);
    }
}

/// <summary>
/// Versioned framework/user extension for a non-standard markup value language. The plugin
/// owns recognition and formatting only; successful parsing must return the canonical shared
/// syntax model consumed by the normal infoset, Roslyn binder, IR, and emitter.
/// </summary>
public interface IXamlMarkupSyntaxPlugin
{
    string Id { get; }
    int ContractVersion { get; }
    int Version { get; }
    int Priority { get; }
    XamlMarkupSyntaxContexts Contexts { get; }
    XamlMarkupSyntaxAssociativity Associativity { get; }
    XamlMarkupSyntaxConflictPolicy ConflictPolicy { get; }
    XamlMarkupSyntaxCapabilities Capabilities { get; }
    IReadOnlyList<char> TriggerCharacters { get; }
    IXamlMarkupTokenRecognizer? TokenRecognizer { get; }

    XamlMarkupSyntaxPluginResult Parse(XamlMarkupSyntaxPluginContext context);

    bool TryFormat(XamlMarkupExtension extension, out string text);
}

/// <summary>
/// Immutable, thread-safe registry for custom markup syntax. Registration validation is
/// fail-fast; runtime overlap is resolved by priority and diagnosed at equal priority.
/// </summary>
public sealed class XamlMarkupLanguage
{
    public const int CurrentContractVersion = 1;

    private readonly ImmutableArray<IXamlMarkupSyntaxPlugin> _plugins;
    private readonly ImmutableArray<IXamlMarkupTokenRecognizer> _tokenRecognizers;
    private readonly ImmutableDictionary<char, ImmutableArray<IXamlMarkupSyntaxPlugin>> _byTrigger;
    private readonly ImmutableDictionary<string, IXamlMarkupSyntaxPlugin> _byId;

    private XamlMarkupLanguage(
        ImmutableArray<IXamlMarkupSyntaxPlugin> plugins,
        ImmutableArray<IXamlMarkupTokenRecognizer> tokenRecognizers,
        ImmutableDictionary<char, ImmutableArray<IXamlMarkupSyntaxPlugin>> byTrigger,
        ImmutableDictionary<string, IXamlMarkupSyntaxPlugin> byId)
    {
        _plugins = plugins;
        _tokenRecognizers = tokenRecognizers;
        _byTrigger = byTrigger;
        _byId = byId;
    }

    public static XamlMarkupLanguage Empty { get; } = Create(Array.Empty<IXamlMarkupSyntaxPlugin>());

    public int ContractVersion => CurrentContractVersion;
    public ImmutableArray<IXamlMarkupSyntaxPlugin> Plugins => _plugins;
    public ImmutableArray<IXamlMarkupTokenRecognizer> TokenRecognizers => _tokenRecognizers;

    public static XamlMarkupLanguage Create(IEnumerable<IXamlMarkupSyntaxPlugin> plugins)
    {
        if (plugins == null) throw new ArgumentNullException(nameof(plugins));

        var ordered = new List<IXamlMarkupSyntaxPlugin>();
        var byId = new Dictionary<string, IXamlMarkupSyntaxPlugin>(StringComparer.Ordinal);
        foreach (var candidate in plugins)
        {
            if (candidate == null)
                throw new ArgumentException("A markup language cannot contain a null plugin.", nameof(plugins));
            Validate(candidate);
            var plugin = new RegisteredSyntaxPlugin(candidate);
            if (byId.ContainsKey(plugin.Id))
                throw new ArgumentException(
                    $"Markup syntax plugin ID '{plugin.Id}' is registered more than once.",
                    nameof(plugins));
            byId.Add(plugin.Id, plugin);
            ordered.Add(plugin);
        }

        ordered.Sort(ComparePlugins);
        var triggerLists = new Dictionary<char, List<IXamlMarkupSyntaxPlugin>>();
        var recognizers = ImmutableArray.CreateBuilder<IXamlMarkupTokenRecognizer>();
        foreach (var plugin in ordered)
        {
            foreach (var trigger in plugin.TriggerCharacters)
            {
                if (!triggerLists.TryGetValue(trigger, out var list))
                {
                    list = new List<IXamlMarkupSyntaxPlugin>();
                    triggerLists.Add(trigger, list);
                }
                list.Add(plugin);
            }

            if (plugin.TokenRecognizer != null)
                recognizers.Add(new PluginTokenRecognizer(plugin));
        }

        var immutableTriggers =
            ImmutableDictionary.CreateBuilder<char, ImmutableArray<IXamlMarkupSyntaxPlugin>>();
        foreach (var pair in triggerLists)
            immutableTriggers.Add(pair.Key, pair.Value.ToImmutableArray());

        return new XamlMarkupLanguage(
            ordered.ToImmutableArray(),
            recognizers.ToImmutable(),
            immutableTriggers.ToImmutable(),
            byId.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public bool CanStartWith(char character) => _byTrigger.ContainsKey(character);

    public bool TryGetPlugin(string id, out IXamlMarkupSyntaxPlugin plugin)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        return _byId.TryGetValue(id, out plugin!);
    }

    internal ImmutableArray<IXamlMarkupSyntaxPlugin> GetCandidates(
        char trigger,
        XamlMarkupSyntaxContexts context)
    {
        if (!_byTrigger.TryGetValue(trigger, out var candidates))
            return ImmutableArray<IXamlMarkupSyntaxPlugin>.Empty;
        if (context == XamlMarkupSyntaxContexts.None)
            return ImmutableArray<IXamlMarkupSyntaxPlugin>.Empty;

        var filtered = ImmutableArray.CreateBuilder<IXamlMarkupSyntaxPlugin>();
        foreach (var candidate in candidates)
            if ((candidate.Contexts & context) != 0)
                filtered.Add(candidate);
        return filtered.ToImmutable();
    }

    internal static bool AreEquivalent(
        XamlMarkupExtension left,
        XamlMarkupExtension right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
            left.PositionalArguments.Count != right.PositionalArguments.Count ||
            left.NamedArguments.Count != right.NamedArguments.Count)
            return false;

        for (var index = 0; index < left.PositionalArguments.Count; index++)
            if (!AreEquivalent(left.PositionalArguments[index], right.PositionalArguments[index]))
                return false;
        for (var index = 0; index < left.NamedArguments.Count; index++)
        {
            var leftArgument = left.NamedArguments[index];
            var rightArgument = right.NamedArguments[index];
            if (!string.Equals(leftArgument.Name, rightArgument.Name, StringComparison.Ordinal) ||
                !AreEquivalent(leftArgument.Value, rightArgument.Value))
                return false;
        }
        return true;
    }

    private static bool AreEquivalent(XamlMarkupValue left, XamlMarkupValue right)
    {
        if (left is XamlMarkupTextValue leftText &&
            right is XamlMarkupTextValue rightText)
            return string.Equals(leftText.Text, rightText.Text, StringComparison.Ordinal);
        if (left is XamlMarkupExtensionValue leftExtension &&
            right is XamlMarkupExtensionValue rightExtension)
            return AreEquivalent(leftExtension.Extension, rightExtension.Extension);
        return false;
    }

    private static int ComparePlugins(
        IXamlMarkupSyntaxPlugin left,
        IXamlMarkupSyntaxPlugin right)
    {
        var priority = right.Priority.CompareTo(left.Priority);
        if (priority != 0) return priority;
        var id = StringComparer.Ordinal.Compare(left.Id, right.Id);
        return id != 0 ? id : right.Version.CompareTo(left.Version);
    }

    private static void Validate(IXamlMarkupSyntaxPlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin.Id))
            throw new ArgumentException("A markup syntax plugin requires a non-empty ID.", nameof(plugin));
        if (plugin.ContractVersion != CurrentContractVersion)
            throw new ArgumentException(
                $"Markup syntax plugin '{plugin.Id}' requires contract version " +
                $"{plugin.ContractVersion}; this host supports {CurrentContractVersion}.",
                nameof(plugin));
        if (plugin.Version <= 0)
            throw new ArgumentException(
                $"Markup syntax plugin '{plugin.Id}' requires a positive implementation version.",
                nameof(plugin));
        if (plugin.Contexts == XamlMarkupSyntaxContexts.None)
            throw new ArgumentException(
                $"Markup syntax plugin '{plugin.Id}' does not declare a parse context.",
                nameof(plugin));
        if ((plugin.Capabilities &
             (XamlMarkupSyntaxCapabilities.Parse | XamlMarkupSyntaxCapabilities.CanonicalProjection)) !=
            (XamlMarkupSyntaxCapabilities.Parse | XamlMarkupSyntaxCapabilities.CanonicalProjection))
            throw new ArgumentException(
                $"Markup syntax plugin '{plugin.Id}' must declare Parse and CanonicalProjection capabilities.",
                nameof(plugin));
        if (plugin.TriggerCharacters == null || plugin.TriggerCharacters.Count == 0)
            throw new ArgumentException(
                $"Markup syntax plugin '{plugin.Id}' requires at least one trigger character.",
                nameof(plugin));

        var triggers = new HashSet<char>();
        foreach (var trigger in plugin.TriggerCharacters)
        {
            if (char.IsWhiteSpace(trigger) || trigger == '\0')
                throw new ArgumentException(
                    $"Markup syntax plugin '{plugin.Id}' contains an invalid trigger character.",
                    nameof(plugin));
            if (!triggers.Add(trigger))
                throw new ArgumentException(
                    $"Markup syntax plugin '{plugin.Id}' repeats trigger '{trigger}'.",
                    nameof(plugin));
        }

        if (plugin.TokenRecognizer != null &&
            (plugin.Capabilities & XamlMarkupSyntaxCapabilities.Tokenize) == 0)
            throw new ArgumentException(
                $"Markup syntax plugin '{plugin.Id}' supplies a token recognizer without Tokenize capability.",
                nameof(plugin));
        if (plugin.TokenRecognizer == null &&
            (plugin.Capabilities & XamlMarkupSyntaxCapabilities.Tokenize) != 0)
            throw new ArgumentException(
                $"Markup syntax plugin '{plugin.Id}' declares Tokenize capability without a recognizer.",
                nameof(plugin));
    }

    private sealed class PluginTokenRecognizer :
        IXamlMarkupTokenRecognizer,
        IXamlMarkupTokenConflictPolicyProvider
    {
        private readonly IXamlMarkupSyntaxPlugin _plugin;
        private readonly IXamlMarkupTokenRecognizer _recognizer;

        public PluginTokenRecognizer(IXamlMarkupSyntaxPlugin plugin)
        {
            _plugin = plugin;
            _recognizer = plugin.TokenRecognizer!;
        }

        public string Id => _plugin.Id;
        public int Version => _plugin.Version;
        public int Priority => _plugin.Priority;
        public IReadOnlyList<char> TriggerCharacters => _plugin.TriggerCharacters;
        public XamlMarkupSyntaxConflictPolicy ConflictPolicy => _plugin.ConflictPolicy;

        public bool TryRecognize(
            SourceText source,
            TextSpan remaining,
            out XamlMarkupTokenRecognition recognition) =>
            _recognizer.TryRecognize(source, remaining, out recognition);
    }

    private sealed class RegisteredSyntaxPlugin : IXamlMarkupSyntaxPlugin
    {
        private readonly IXamlMarkupSyntaxPlugin _plugin;

        public RegisteredSyntaxPlugin(IXamlMarkupSyntaxPlugin plugin)
        {
            _plugin = plugin;
            Id = plugin.Id;
            ContractVersion = plugin.ContractVersion;
            Version = plugin.Version;
            Priority = plugin.Priority;
            Contexts = plugin.Contexts;
            Associativity = plugin.Associativity;
            ConflictPolicy = plugin.ConflictPolicy;
            Capabilities = plugin.Capabilities;
            TriggerCharacters = plugin.TriggerCharacters.ToImmutableArray();
            TokenRecognizer = plugin.TokenRecognizer;
        }

        public string Id { get; }
        public int ContractVersion { get; }
        public int Version { get; }
        public int Priority { get; }
        public XamlMarkupSyntaxContexts Contexts { get; }
        public XamlMarkupSyntaxAssociativity Associativity { get; }
        public XamlMarkupSyntaxConflictPolicy ConflictPolicy { get; }
        public XamlMarkupSyntaxCapabilities Capabilities { get; }
        public IReadOnlyList<char> TriggerCharacters { get; }
        public IXamlMarkupTokenRecognizer? TokenRecognizer { get; }

        public XamlMarkupSyntaxPluginResult Parse(XamlMarkupSyntaxPluginContext context) =>
            _plugin.Parse(context);

        public bool TryFormat(XamlMarkupExtension extension, out string text) =>
            _plugin.TryFormat(extension, out text);
    }
}
