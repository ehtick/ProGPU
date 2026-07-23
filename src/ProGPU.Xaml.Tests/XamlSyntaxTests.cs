using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Editing;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Syntax;
using System.Text;
using Xunit;

namespace ProGPU.Xaml.Tests;

public sealed class XamlSyntaxTests
{
    [Fact]
    public void LosslessTokensReconstructDocument()
    {
        const string source = "<?xml version=\"1.0\"?><Page xmlns='urn:test' A=\"&amp;\"><!--c--><![CDATA[t]]></Page>";
        var tree = XamlParser.Parse(SourceText.From(source), "sample.xaml",
            new XamlParseOptions { Mode = XamlParseMode.Recovering });
        var reconstructed = string.Concat(tree.Tokens
            .Where(token => token.Kind != ProGPU.Xaml.Syntax.XamlTokenKind.EndOfFile)
            .Select(token => token.Text));
        Assert.Equal(source, reconstructed);
    }

    [Fact]
    public void AnnotationsDoNotMutatePublishedNode()
    {
        var root = XamlParser.Parse(SourceText.From("<Page/>"), "sample.xaml").GetRoot()!;
        var annotation = new SyntaxAnnotation("test", "value");
        var annotated = root.WithAdditionalAnnotations(annotation);
        Assert.False(root.HasAnnotation(annotation));
        Assert.True(annotated.HasAnnotation(annotation));
        Assert.NotSame(root, annotated);
    }

    [Fact]
    public void BatchedEditorReturnsRoslynTextChangesAndNewTree()
    {
        var tree = XamlParser.Parse(SourceText.From("<Page><Old/></Page>"), "sample.xaml");
        var old = Assert.IsType<ProGPU.Xaml.Syntax.XamlObjectSyntax>(tree.GetRoot()!.Children.Single());
        var editor = new XamlSyntaxEditor(tree);
        editor.ReplaceNode(old, "<New/>");
        var changed = editor.GetChangedTree();
        Assert.Equal("<Page><New/></Page>", changed.GetText().ToString());
        Assert.Single(editor.GetTextChanges());
    }

    [Fact]
    public void FluentGenericXamlParsesUnchanged()
    {
        var repository = FindRepositoryRoot();
        var path = Path.Combine(repository, "external", "microsoft-ui-xaml", "src", "dxaml", "xcp", "dxaml", "themes", "generic.xaml");
        if (!File.Exists(path)) return;
        var source = SourceText.From(File.ReadAllText(path));
        var tree = XamlParser.Parse(source, path);
        Assert.DoesNotContain(tree.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(source.ToString(), string.Concat(tree.Tokens
            .Where(token => token.Kind != ProGPU.Xaml.Syntax.XamlTokenKind.EndOfFile)
            .Select(token => token.Text)));
    }

    [Fact]
    public void RecoveringParserPreservesEveryDeterministicFuzzInput()
    {
        const string alphabet =
            "<>/='\"!?[]&;:-_ abcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random(0x58414D4C);
        for (var iteration = 0; iteration < 256; iteration++)
        {
            var length = random.Next(0, 1024);
            var source = new char[length];
            for (var index = 0; index < source.Length; index++)
                source[index] = alphabet[random.Next(alphabet.Length)];
            var text = new string(source);

            var tree = XamlParser.Parse(
                SourceText.From(text),
                "fuzz-" + iteration + ".xaml",
                new XamlParseOptions
                {
                    Mode = XamlParseMode.Recovering,
                    MaximumDiagnostics = 64
                });

            Assert.Equal(
                text,
                string.Concat(tree.Tokens
                    .Where(token => token.Kind != XamlTokenKind.EndOfFile)
                    .Select(token => token.Text)));
            Assert.True(tree.GetDiagnostics().Length <= 64);
        }
    }

    [Fact]
    public void ParserRejectsUnsafeLimitsAndSnapshotsMutableOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XamlParser.Parse(
            SourceText.From("<Page/>"),
            options: new XamlParseOptions { MaximumDepth = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => XamlParser.Parse(
            SourceText.From("<Page/>"),
            options: new XamlParseOptions
            {
                MaximumDepth = XamlParseOptions.MaximumSupportedDepth + 1
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() => XamlParser.Parse(
            SourceText.From("<Page/>"),
            options: new XamlParseOptions { MaximumTokens = 0 }));
        Assert.Throws<ArgumentException>(() => XamlParser.Parse(
            SourceText.From("<Page/>"),
            options: new XamlParseOptions
            {
                Extensions = new IXamlSyntaxExtension[] { null! }
            }));

        var options = new XamlParseOptions
        {
            Mode = XamlParseMode.Recovering,
            MaximumDepth = 8
        };
        var tree = XamlParser.Parse(
            SourceText.From("<Page/>"),
            options: options);
        options.MaximumDepth = 0;
        var changed = tree.WithChangedText(SourceText.From("<Page A=\"1\"/>"));
        Assert.NotNull(changed.GetRoot());
    }

    [Fact]
    public void ExcessiveDocumentDepthIsDiagnosedWithoutLosingSource()
    {
        const int maximumDepth = 32;
        var source = new StringBuilder();
        for (var index = 0; index < maximumDepth * 4; index++)
            source.Append("<N>");
        for (var index = 0; index < maximumDepth * 4; index++)
            source.Append("</N>");
        var text = source.ToString();

        var tree = XamlParser.Parse(
            SourceText.From(text),
            "deep.xaml",
            new XamlParseOptions
            {
                Mode = XamlParseMode.Recovering,
                MaximumDepth = maximumDepth
            });

        Assert.Contains(
            tree.GetDiagnostics(),
            diagnostic => diagnostic.Id == "PGXAML1004");
        Assert.Equal(
            text,
            string.Concat(tree.Tokens
                .Where(token => token.Kind != XamlTokenKind.EndOfFile)
                .Select(token => token.Text)));
    }

    [Fact]
    public void LongTokenLexingObservesCancellationWithinTheToken()
    {
        using var cancellation = new CancellationTokenSource();
        var source = "<Page A=\"" + new string('x', 128 * 1024) + "\"/>";
        var text = new CancelingSourceText(
            source,
            cancelAt: 32 * 1024,
            cancellation);

        Assert.Throws<OperationCanceledException>(() => XamlParser.Parse(
            text,
            "cancel.xaml",
            new XamlParseOptions { Mode = XamlParseMode.Recovering },
            cancellation.Token));
    }

    [Fact]
    public void LargeTextTokenHasBoundedParserAllocations()
    {
        var text = SourceText.From(
            "<Page xmlns=\"urn:test\">" +
            new string('x', 1024 * 1024) +
            "</Page>");
        _ = XamlParser.Parse(SourceText.From("<Warmup/>"));
        var before = GC.GetAllocatedBytesForCurrentThread();

        var tree = XamlParser.Parse(
            text,
            "large.xaml",
            new XamlParseOptions { Mode = XamlParseMode.Recovering });

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.DoesNotContain(
            tree.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(
            allocated <= text.Length * 32L,
            $"Parser allocated {allocated} bytes for {text.Length} source characters.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class CancelingSourceText : SourceText
    {
        private readonly string _text;
        private readonly int _cancelAt;
        private readonly CancellationTokenSource _cancellation;

        public CancelingSourceText(
            string text,
            int cancelAt,
            CancellationTokenSource cancellation)
        {
            _text = text;
            _cancelAt = cancelAt;
            _cancellation = cancellation;
        }

        public override Encoding Encoding => System.Text.Encoding.UTF8;
        public override int Length => _text.Length;

        public override char this[int position]
        {
            get
            {
                if (position >= _cancelAt)
                    _cancellation.Cancel();
                return _text[position];
            }
        }

        public override void CopyTo(
            int sourceIndex,
            char[] destination,
            int destinationIndex,
            int count) =>
            _text.CopyTo(sourceIndex, destination, destinationIndex, count);
    }
}
