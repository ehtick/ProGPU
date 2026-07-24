using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Formatting;
using ProGPU.Xaml.Generation;
using ProGPU.Xaml.Infoset;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Syntax;
using Xunit;

namespace ProGPU.Xaml.Tests;

public sealed class XamlMarkupTests
{
    [Fact]
    public void TokenizerIsLosslessAndNestedParserIsFrameworkNeutral()
    {
        const string value = "{Binding Path={x:Static local:State.Name}, Mode=TwoWay}";
        var source = SourceText.From(value);
        var result = new XamlMarkupExtensionParser().Parse(source, new TextSpan(0, source.Length));
        Assert.False(result.HasErrors);
        Assert.Equal(value, string.Concat(result.Tokens
            .Where(token => token.Kind != XamlMarkupTokenKind.EndOfFile)
            .Select(token => token.Text)));
        Assert.Equal("Binding", result.Root!.Name);
        Assert.IsType<XamlMarkupExtensionValue>(result.Root.NamedArguments[0].Value);
    }

    [Fact]
    public void FormatterProducesRoslynTextChangesAndIsIdempotent()
    {
        var source = SourceText.From("{Binding  Path = Name,  Mode = TwoWay } ");
        var parser = new XamlMarkupExtensionParser();
        var syntax = parser.Parse(source, new TextSpan(0, source.Length));
        var formatted = XamlMarkupFormatter.Format(syntax, source);
        Assert.Equal("{Binding Path=Name, Mode=TwoWay} ", formatted.ToString());
        var reparsed = parser.Parse(formatted, new TextSpan(0, formatted.Length));
        Assert.Empty(XamlMarkupFormatter.GetTextChanges(reparsed, formatted));
    }

    [Fact]
    public void GeneratorEscapesAndRoundTripsNestedValues()
    {
        var nested = new XamlMarkupExtension("StaticResource",
            new XamlMarkupValue[] { new XamlMarkupTextValue("key,with comma") },
            Array.Empty<XamlMarkupNamedArgument>());
        var root = new XamlMarkupExtension("Binding",
            Array.Empty<XamlMarkupValue>(),
            new[]
            {
                new XamlMarkupNamedArgument("Source", new XamlMarkupExtensionValue(nested)),
                new XamlMarkupNamedArgument("Path", new XamlMarkupTextValue("A B"))
            });
        var generated = XamlMarkupSyntaxGenerator.Generate(root);
        var parsed = new XamlMarkupExtensionParser().Parse(generated, new TextSpan(0, generated.Length));
        Assert.False(parsed.HasErrors);
        Assert.Equal("A B", ((XamlMarkupTextValue)parsed.Root!.NamedArguments[1].Value).Text);
    }

    [Fact]
    public void TriggerIndexedCustomRecognizerProducesCustomToken()
    {
        var source = SourceText.From("%name");
        var result = XamlMarkupTokenizer.Tokenize(source, new TextSpan(0, source.Length), options:
            new XamlMarkupParseOptions { TokenRecognizers = new[] { new PercentRecognizer() } });
        Assert.True(result.Tokens[0].IsCustom);
        Assert.Equal("%name", result.Tokens[0].Text);
    }

    [Fact]
    public void VersionedSyntaxPluginProjectsIntoCanonicalMarkupAstAndRoundTrips()
    {
        var plugin = new AtBindingSyntaxPlugin("test.at-binding", priority: 20);
        var language = XamlMarkupLanguage.Create(new[] { plugin });
        var source = SourceText.From("@Customer.Name");
        var options = new XamlMarkupParseOptions
        {
            SyntaxLanguage = language,
            Context = XamlMarkupSyntaxContexts.Standalone
        };

        var parsed = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: options);

        Assert.False(parsed.HasErrors, string.Join(Environment.NewLine, parsed.Diagnostics));
        Assert.True(parsed.IsMarkupExtension);
        Assert.Equal(plugin.Id, parsed.SyntaxPluginId);
        Assert.Equal("Binding", parsed.Root!.Name);
        var path = Assert.Single(parsed.Root.NamedArguments);
        Assert.Equal("Path", path.Name);
        Assert.Equal("Customer.Name", Assert.IsType<XamlMarkupTextValue>(path.Value).Text);
        Assert.True(parsed.Tokens[0].IsCustom);

        var generated = XamlMarkupSyntaxGenerator.Generate(
            parsed.Root,
            language,
            preferredPluginId: plugin.Id);
        Assert.Equal("@Customer.Name", generated.ToString());
        var formatted = XamlMarkupFormatter.Format(parsed, source, language: language);
        Assert.Equal("@Customer.Name", formatted.ToString());
    }

    [Fact]
    public void InfosetUsesRegisteredAttributeSyntaxWithoutDialectParserFork()
    {
        var language = XamlMarkupLanguage.Create(new[]
        {
            new AtBindingSyntaxPlugin("test.at-binding", priority: 20)
        });
        const string xaml =
            "<TextBlock xmlns=\"using:Test\" Text=\"@Customer.Name\" />";
        var syntax = XamlParser.Parse(SourceText.From(xaml), "Custom.xaml");
        var infoset = new XamlInfosetConverter().Convert(
            syntax.Document,
            new XamlInfosetConversionOptions
            {
                MarkupOptions = new XamlMarkupParseOptions
                {
                    SyntaxLanguage = language
                }
            });

        Assert.False(infoset.HasErrors, string.Join(Environment.NewLine, infoset.Diagnostics));
        var textMember = Assert.Single(infoset.Root!.Members.Where(
            static member => member.Name.LocalName == "Text"));
        var binding = Assert.IsType<XamlInfosetObject>(Assert.Single(textMember.Values));
        Assert.True(binding.IsMarkupExtension);
        Assert.Equal("Binding", binding.TypeName.LocalName);
        Assert.Equal(
            "Customer.Name",
            Assert.IsType<XamlInfosetText>(
                Assert.Single(Assert.Single(binding.Members).Values)).Text);
    }

    [Fact]
    public void SyntaxPluginPriorityIsDeterministicAndEqualPriorityOverlapIsDiagnosed()
    {
        var selectedLanguage = XamlMarkupLanguage.Create(new IXamlMarkupSyntaxPlugin[]
        {
            new AtBindingSyntaxPlugin("test.low", priority: 10, canonicalName: "Binding"),
            new AtBindingSyntaxPlugin("test.high", priority: 20, canonicalName: "x:Bind")
        });
        var source = SourceText.From("@Name");
        var selected = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: new XamlMarkupParseOptions { SyntaxLanguage = selectedLanguage });
        Assert.False(selected.HasErrors);
        Assert.Equal("test.high", selected.SyntaxPluginId);
        Assert.Equal("x:Bind", selected.Root!.Name);

        var ambiguousLanguage = XamlMarkupLanguage.Create(new IXamlMarkupSyntaxPlugin[]
        {
            new AtBindingSyntaxPlugin("test.a", priority: 20, canonicalName: "Binding"),
            new AtBindingSyntaxPlugin("test.b", priority: 20, canonicalName: "x:Bind")
        });
        var ambiguous = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: new XamlMarkupParseOptions { SyntaxLanguage = ambiguousLanguage });
        Assert.True(ambiguous.HasErrors);
        Assert.Null(ambiguous.Root);
        Assert.Contains(ambiguous.Diagnostics, static diagnostic =>
            diagnostic.Id == "PGXAML1154");
    }

    [Fact]
    public void EquivalentSyntaxPluginsCanCoalesceAndInvalidRegistrationsFailFast()
    {
        var language = XamlMarkupLanguage.Create(new IXamlMarkupSyntaxPlugin[]
        {
            new AtBindingSyntaxPlugin(
                "test.a",
                priority: 20,
                conflictPolicy: XamlMarkupSyntaxConflictPolicy.CoalesceEquivalent),
            new AtBindingSyntaxPlugin(
                "test.b",
                priority: 20,
                conflictPolicy: XamlMarkupSyntaxConflictPolicy.CoalesceEquivalent)
        });
        var source = SourceText.From("@Name");
        var parsed = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: new XamlMarkupParseOptions { SyntaxLanguage = language });
        Assert.False(parsed.HasErrors);
        Assert.Equal("test.a", parsed.SyntaxPluginId);

        Assert.Throws<ArgumentException>(() => XamlMarkupLanguage.Create(
            new IXamlMarkupSyntaxPlugin[]
            {
                new AtBindingSyntaxPlugin("duplicate", priority: 1),
                new AtBindingSyntaxPlugin("duplicate", priority: 2)
            }));
        Assert.Throws<ArgumentException>(() => XamlMarkupLanguage.Create(
            new[] { new AtBindingSyntaxPlugin("wrong.contract", priority: 1, contractVersion: 2) }));
    }

    [Fact]
    public void SyntaxPluginContextsFailuresAndExceptionsAreExplicit()
    {
        var attributeOnly = XamlMarkupLanguage.Create(new[]
        {
            new AtBindingSyntaxPlugin(
                "test.attribute-only",
                priority: 10,
                contexts: XamlMarkupSyntaxContexts.AttributeValue)
        });
        var source = SourceText.From("@Name");
        var standalone = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: new XamlMarkupParseOptions
            {
                SyntaxLanguage = attributeOnly,
                Context = XamlMarkupSyntaxContexts.Standalone
            });
        Assert.False(standalone.IsMarkupExtension);
        Assert.Equal(
            "@Name",
            string.Concat(standalone.Tokens
                .Where(static token => token.Kind != XamlMarkupTokenKind.EndOfFile)
                .Select(static token => token.Text)));

        var attribute = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: new XamlMarkupParseOptions
            {
                SyntaxLanguage = attributeOnly,
                Context = XamlMarkupSyntaxContexts.AttributeValue
            });
        Assert.False(attribute.HasErrors);
        Assert.True(attribute.IsMarkupExtension);

        var empty = SourceText.From("@");
        var malformed = new XamlMarkupExtensionParser().Parse(
            empty,
            new TextSpan(0, empty.Length),
            options: new XamlMarkupParseOptions
            {
                SyntaxLanguage = attributeOnly,
                Context = XamlMarkupSyntaxContexts.AttributeValue
            });
        Assert.Contains(malformed.Diagnostics, static diagnostic =>
            diagnostic.Id == "PGXAML1156");

        var throwingLanguage = XamlMarkupLanguage.Create(
            new IXamlMarkupSyntaxPlugin[] { new ThrowingSyntaxPlugin() });
        var failed = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: new XamlMarkupParseOptions { SyntaxLanguage = throwingLanguage });
        Assert.Contains(failed.Diagnostics, static diagnostic =>
            diagnostic.Id == "PGXAML1155");
        Assert.False(failed.IsMarkupExtension);
    }

    [Fact]
    public void SyntaxPluginParsingPropagatesCancellation()
    {
        var language = XamlMarkupLanguage.Create(new[]
        {
            new AtBindingSyntaxPlugin("test.cancel", priority: 10)
        });
        var source = SourceText.From("@Name");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new XamlMarkupExtensionParser().Parse(
                source,
                new TextSpan(0, source.Length),
                options: new XamlMarkupParseOptions { SyntaxLanguage = language },
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ConfiguredBracketPairsProtectNestedCommasAndEquals()
    {
        const string value =
            "{Binding Path=Items[(key,value)=selected], Mode=OneWay}";
        var source = SourceText.From(value);
        var options = new XamlMarkupParseOptions
        {
            BracketPairs = new Dictionary<char, char>
            {
                ['['] = ']',
                ['('] = ')'
            }
        };
        var result = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: options);
        Assert.False(result.HasErrors, string.Join(
            Environment.NewLine,
            result.Diagnostics));
        Assert.Collection(
            result.Root!.NamedArguments,
            argument =>
            {
                Assert.Equal("Path", argument.Name);
                Assert.Equal(
                    "Items[(key,value)=selected]",
                    Assert.IsType<XamlMarkupTextValue>(argument.Value).Text);
            },
            argument =>
            {
                Assert.Equal("Mode", argument.Name);
                Assert.Equal(
                    "OneWay",
                    Assert.IsType<XamlMarkupTextValue>(argument.Value).Text);
            });
        Assert.Equal(
            value,
            string.Concat(result.Tokens
                .Where(token => token.Kind != XamlMarkupTokenKind.EndOfFile)
                .Select(token => token.Text)));

        var malformed = SourceText.From(
            "{Binding Path=Items[(key,value), Mode=OneWay}");
        Assert.True(new XamlMarkupExtensionParser().Parse(
            malformed,
            new TextSpan(0, malformed.Length),
            options: options).HasErrors);
    }

    [Fact]
    public void TypeNameParserSupportsNestedGenericArgumentsAndRecovery()
    {
        const string text = "local:Pair(x:String, local:Box(x:Int32)), x:Boolean";
        var result = new XamlTypeNameParser().Parse(text);
        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Types.Length);
        Assert.Equal("local:Pair", result.Types[0].QualifiedName);
        Assert.Equal(2, result.Types[0].TypeArguments.Length);
        Assert.Equal("local:Box", result.Types[0].TypeArguments[1].QualifiedName);
        Assert.Equal("x:Int32", Assert.Single(result.Types[0].TypeArguments[1].TypeArguments).QualifiedName);
        Assert.Equal(text.IndexOf("local:Pair", StringComparison.Ordinal), result.Types[0].Span.Start);

        var malformed = new XamlTypeNameParser().Parse("local:Pair(x:String,");
        Assert.True(malformed.HasErrors);
        Assert.Contains(malformed.Diagnostics, diagnostic =>
            diagnostic.Id == "PGXAML1160" && diagnostic.Properties["MSXamlSection"] == "7.4.16");

        Assert.True(new XamlTypeNameParser().Parse("local:Pair()").HasErrors);
        Assert.True(new XamlTypeNameParser().Parse("local:Pair(x:String,)").HasErrors);
        Assert.True(new XamlTypeNameParser().Parse("x:String,").HasErrors);
    }

    [Fact]
    public void BindingPathParserPreservesTokensAndProducesStableMemberSegments()
    {
        const string path = "  ViewModel . Customer_1 . DisplayName  ";
        var syntax = new XamlBindingPathParser().Parse(path);

        Assert.False(syntax.HasErrors);
        Assert.Equal(
            new[] { "ViewModel", "Customer_1", "DisplayName" },
            syntax.Segments.Select(segment => segment.Text));
        Assert.Equal(
            "ViewModel.Customer_1.DisplayName",
            string.Concat(syntax.Tokens
                .Where(token => token.Kind is
                    XamlBindingPathTokenKind.Identifier or XamlBindingPathTokenKind.Dot)
                .Select(token => token.Text)));
        Assert.Equal(
            path,
            string.Concat(syntax.Tokens
                .Where(token => token.Kind != XamlBindingPathTokenKind.EndOfInput)
                .Select(token => token.Text)));
        Assert.Equal(path.IndexOf("ViewModel", StringComparison.Ordinal), syntax.Segments[0].Span.Start);
    }

    [Fact]
    public void BindingPathParserProducesTypedLosslessIndexerSteps()
    {
        const string path = " Teams [ 2 ] . Players [ 'John ^'Smith' ] . Name ";
        var syntax = new XamlBindingPathParser().Parse(path);

        Assert.False(syntax.HasErrors);
        Assert.Equal(
            new[]
            {
                XamlBindingPathStepKind.Member,
                XamlBindingPathStepKind.IntegerIndexer,
                XamlBindingPathStepKind.Member,
                XamlBindingPathStepKind.StringIndexer,
                XamlBindingPathStepKind.Member
            },
            syntax.Steps.Select(static step => step.Kind));
        Assert.Equal(2, syntax.Steps[1].IntegerValue);
        Assert.Equal("John 'Smith", syntax.Steps[3].StringValue);
        Assert.Equal(
            path,
            string.Concat(syntax.Tokens
                .Where(token => token.Kind != XamlBindingPathTokenKind.EndOfInput)
                .Select(token => token.Text)));
        Assert.Equal("[ 2 ]", path.Substring(
            syntax.Steps[1].Span.Start,
            syntax.Steps[1].Span.Length));
    }

    [Theory]
    [InlineData(
        "((local:Derived)Model).Name",
        "Member:Model|Cast:local:Derived|Member:Name")]
    [InlineData(
        "Model.(local:Derived.Name)",
        "Member:Model|QualifiedMember:local:Derived.Name")]
    [InlineData(
        "Element.(Grid.Row)",
        "Member:Element|QualifiedMember:Grid.Row")]
    [InlineData(
        "(local:Item).Name",
        "Cast:local:Item|Member:Name")]
    public void BindingPathParserFlattensGroupedCastsAndQualifiedMembers(
        string path,
        string expected)
    {
        var syntax = new XamlBindingPathParser().Parse(path);

        Assert.False(syntax.HasErrors, string.Join(", ", syntax.ErrorSpans));
        Assert.Equal(
            expected,
            string.Join(
                "|",
                syntax.Steps.Select(static step => step.Kind switch
                {
                    XamlBindingPathStepKind.Cast =>
                        "Cast:" + step.TypeName,
                    XamlBindingPathStepKind.QualifiedMember =>
                        "QualifiedMember:" + step.TypeName + "." + step.MemberName,
                    _ => step.Kind + ":" + step.ValueToken.Text
                })));
        Assert.Equal(
            path,
            string.Concat(syntax.Tokens
                .Where(token => token.Kind != XamlBindingPathTokenKind.EndOfInput)
                .Select(token => token.Text)));
    }

    [Theory]
    [InlineData(
        "Format(Items[0].Title, 'prefix ^'value', -2.5, x:True)",
        false,
        null,
        "Format",
        "BindingPath|StringLiteral|NumericLiteral|BooleanLiteral")]
    [InlineData(
        "ViewModel.Format(Name, 42)",
        false,
        null,
        "Format",
        "BindingPath|NumericLiteral")]
    [InlineData(
        "local:Formatter.Format(Name, x:False)",
        true,
        "local:Formatter",
        "Format",
        "BindingPath|BooleanLiteral")]
    public void BindingPathParserProducesLosslessTypedFunctionCalls(
        string path,
        bool isStatic,
        string? typeName,
        string methodName,
        string argumentKinds)
    {
        var syntax = new XamlBindingPathParser().Parse(path);

        Assert.False(syntax.HasErrors, string.Join(", ", syntax.ErrorSpans));
        var function = Assert.Single(
            syntax.Steps.Where(static step =>
                step.Kind == XamlBindingPathStepKind.FunctionCall));
        Assert.Equal(isStatic, function.IsStaticFunction);
        Assert.Equal(typeName, function.TypeName);
        Assert.Equal(methodName, function.MemberName);
        Assert.Equal(
            argumentKinds,
            string.Join("|", function.Arguments.Select(static argument => argument.Kind)));
        Assert.Equal(
            path,
            string.Concat(syntax.Tokens
                .Where(token => token.Kind != XamlBindingPathTokenKind.EndOfInput)
                .Select(token => token.Text)));
    }

    [Theory]
    [InlineData(".Name")]
    [InlineData("Model..Name")]
    [InlineData("Model Name")]
    [InlineData("Model[]")]
    [InlineData("Model[999999999999999999999]")]
    [InlineData("Model['unterminated]")]
    [InlineData("((local:Type)Model.Name")]
    [InlineData("Model.(Grid.)")]
    [InlineData("Model.(local:.Name)")]
    [InlineData("Model.")]
    [InlineData("Format(Name, )")]
    [InlineData("Format(, Name)")]
    [InlineData("Format(Name")]
    [InlineData("local:Formatter.Format(Name,")]
    public void BindingPathParserRecoversUnsupportedOrMalformedSyntax(string path)
    {
        var syntax = new XamlBindingPathParser().Parse(path);
        Assert.True(syntax.HasErrors);
        Assert.NotEmpty(syntax.Tokens);
        Assert.Equal(XamlBindingPathTokenKind.EndOfInput, syntax.Tokens[^1].Kind);
    }

    private sealed class PercentRecognizer : IXamlMarkupTokenRecognizer
    {
        public string Id => "test.percent";
        public int Version => 1;
        public int Priority => 0;
        public IReadOnlyList<char> TriggerCharacters { get; } = new[] { '%' };
        public bool TryRecognize(SourceText source, TextSpan remaining, out XamlMarkupTokenRecognition recognition)
        {
            var length = 1;
            while (length < remaining.Length && char.IsLetter(source[remaining.Start + length])) length++;
            recognition = new XamlMarkupTokenRecognition((int)XamlMarkupTokenKind.FirstCustom, length);
            return true;
        }
    }

    private sealed class AtBindingSyntaxPlugin : IXamlMarkupSyntaxPlugin
    {
        private readonly string _canonicalName;

        public AtBindingSyntaxPlugin(
            string id,
            int priority,
            string canonicalName = "Binding",
            XamlMarkupSyntaxConflictPolicy conflictPolicy =
                XamlMarkupSyntaxConflictPolicy.Diagnose,
            int contractVersion = XamlMarkupLanguage.CurrentContractVersion,
            bool tokenize = true,
            XamlMarkupSyntaxContexts contexts = XamlMarkupSyntaxContexts.All)
        {
            Id = id;
            Priority = priority;
            _canonicalName = canonicalName;
            ConflictPolicy = conflictPolicy;
            ContractVersion = contractVersion;
            Contexts = contexts;
            TokenRecognizer = tokenize ? new AtTokenRecognizer(id, priority) : null;
        }

        public string Id { get; }
        public int ContractVersion { get; }
        public int Version => 1;
        public int Priority { get; }
        public XamlMarkupSyntaxContexts Contexts { get; }
        public XamlMarkupSyntaxAssociativity Associativity =>
            XamlMarkupSyntaxAssociativity.None;
        public XamlMarkupSyntaxConflictPolicy ConflictPolicy { get; }
        public XamlMarkupSyntaxCapabilities Capabilities =>
            (TokenRecognizer == null
                ? XamlMarkupSyntaxCapabilities.None
                : XamlMarkupSyntaxCapabilities.Tokenize) |
            XamlMarkupSyntaxCapabilities.Parse |
            XamlMarkupSyntaxCapabilities.Format |
            XamlMarkupSyntaxCapabilities.CanonicalProjection;
        public IReadOnlyList<char> TriggerCharacters { get; } = new[] { '@' };
        public IXamlMarkupTokenRecognizer? TokenRecognizer { get; }

        public XamlMarkupSyntaxPluginResult Parse(XamlMarkupSyntaxPluginContext context)
        {
            if (context.Span.Length == 0 || context.Source[context.Span.Start] != '@')
                return XamlMarkupSyntaxPluginResult.NotRecognized;
            var valueSpan = new TextSpan(context.Span.Start + 1, context.Span.Length - 1);
            if (valueSpan.Length == 0)
                return XamlMarkupSyntaxPluginResult.Failure(
                    "A binding path is required after '@'.",
                    context.Span);
            var path = context.Source.ToString(valueSpan);
            return XamlMarkupSyntaxPluginResult.Success(
                new XamlMarkupExtension(
                    _canonicalName,
                    Array.Empty<XamlMarkupValue>(),
                    new[]
                    {
                        new XamlMarkupNamedArgument(
                            "Path",
                            new XamlMarkupTextValue(path, valueSpan),
                            context.Span)
                    },
                    context.Span));
        }

        public bool TryFormat(XamlMarkupExtension extension, out string text)
        {
            text = string.Empty;
            if (!string.Equals(extension.Name, _canonicalName, StringComparison.Ordinal) ||
                extension.PositionalArguments.Count != 0 ||
                extension.NamedArguments.Count != 1 ||
                !string.Equals(extension.NamedArguments[0].Name, "Path", StringComparison.Ordinal) ||
                extension.NamedArguments[0].Value is not XamlMarkupTextValue path)
                return false;
            text = "@" + path.Text;
            return true;
        }
    }

    private sealed class AtTokenRecognizer : IXamlMarkupTokenRecognizer
    {
        public AtTokenRecognizer(string id, int priority)
        {
            Id = id + ".token";
            Priority = priority;
        }

        public string Id { get; }
        public int Version => 1;
        public int Priority { get; }
        public IReadOnlyList<char> TriggerCharacters { get; } = new[] { '@' };

        public bool TryRecognize(
            SourceText source,
            TextSpan remaining,
            out XamlMarkupTokenRecognition recognition)
        {
            if (remaining.Length == 0 || source[remaining.Start] != '@')
            {
                recognition = default;
                return false;
            }
            var length = 1;
            while (length < remaining.Length && !char.IsWhiteSpace(source[remaining.Start + length]))
                length++;
            recognition = new XamlMarkupTokenRecognition(
                (int)XamlMarkupTokenKind.FirstCustom + 1,
                length);
            return true;
        }
    }

    private sealed class ThrowingSyntaxPlugin : IXamlMarkupSyntaxPlugin
    {
        public string Id => "test.throwing";
        public int ContractVersion => XamlMarkupLanguage.CurrentContractVersion;
        public int Version => 1;
        public int Priority => 100;
        public XamlMarkupSyntaxContexts Contexts => XamlMarkupSyntaxContexts.All;
        public XamlMarkupSyntaxAssociativity Associativity =>
            XamlMarkupSyntaxAssociativity.None;
        public XamlMarkupSyntaxConflictPolicy ConflictPolicy =>
            XamlMarkupSyntaxConflictPolicy.Diagnose;
        public XamlMarkupSyntaxCapabilities Capabilities =>
            XamlMarkupSyntaxCapabilities.Parse |
            XamlMarkupSyntaxCapabilities.CanonicalProjection;
        public IReadOnlyList<char> TriggerCharacters { get; } = new[] { '@' };
        public IXamlMarkupTokenRecognizer? TokenRecognizer => null;

        public XamlMarkupSyntaxPluginResult Parse(XamlMarkupSyntaxPluginContext context) =>
            throw new InvalidOperationException("Expected plugin failure.");

        public bool TryFormat(XamlMarkupExtension extension, out string text)
        {
            text = string.Empty;
            return false;
        }
    }
}
