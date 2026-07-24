using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Tooling;
using Xunit;

namespace ProGPU.Xaml.Tests;

public sealed class XamlDocumentInspectionTests
{
    [Fact]
    public void ProjectsCanonicalSyntaxTokensInfosetAndDiagnostics()
    {
        const string source = """
<Page xmlns="urn:test" Title="{Binding Name}">
  <StackPanel>hello</StackPanel>
</Page>
""";

        var inspection = new XamlDocumentInspectionService().Inspect(
            SourceText.From(source),
            "Playground.xaml");

        Assert.Equal("Page", inspection.SyntaxTree.GetRoot()!.QualifiedName);
        Assert.Equal("Page", inspection.Infoset.Root!.TypeName.LocalName);
        Assert.Equal(2, inspection.Statistics.SyntaxObjects);
        Assert.Equal(3, inspection.Statistics.InfosetObjects);
        Assert.Contains(
            inspection.Syntax.Entries,
            entry => entry.Kind == XamlInspectionEntryKind.SyntaxAttribute &&
                     entry.Name == "Title" &&
                     entry.Value == "{Binding Name}");
        Assert.Contains(
            inspection.Tokens.Entries,
            entry => entry.Kind == XamlInspectionEntryKind.Token &&
                     entry.Name == "StringLiteral");
        Assert.Contains(
            inspection.InfosetProjection.Entries,
            entry => entry.Kind == XamlInspectionEntryKind.InfosetObject &&
                     entry.Name == "Binding" &&
                     entry.Value == "markup-extension");
        Assert.All(
            inspection.Syntax.Entries.Where(entry => entry.HasStableId),
            entry => Assert.True(entry.SourceSpan.End <= source.Length));
        Assert.False(inspection.Syntax.IsTruncated);
        Assert.False(inspection.InfosetProjection.IsTruncated);
    }

    [Fact]
    public void RecoveryProjectsDiagnosticsAndBoundsEveryView()
    {
        const string source = "<Page A=\"one\" A=\"two\"><Child/><Other/></Page>";
        var options = new XamlDocumentInspectionOptions
        {
            ParseOptions = new XamlParseOptions
            {
                Mode = XamlParseMode.Recovering
            },
            MaximumProjectionEntries = 2,
            MaximumPreviewLength = 4
        };

        var inspection = new XamlDocumentInspectionService().Inspect(
            SourceText.From(source),
            "Invalid.xaml",
            options);

        Assert.Equal(2, inspection.Syntax.Entries.Length);
        Assert.True(inspection.Syntax.IsTruncated);
        Assert.True(inspection.Tokens.IsTruncated);
        Assert.True(inspection.InfosetProjection.IsTruncated);
        Assert.True(inspection.Statistics.SyntaxObjects > inspection.Syntax.Entries.Length);
        Assert.True(inspection.Statistics.Errors > 0);
        Assert.Contains(
            inspection.Diagnostics.Entries,
            entry => entry.Kind == XamlInspectionEntryKind.Diagnostic &&
                     entry.Name.StartsWith("PGXAML", StringComparison.Ordinal));
        Assert.All(
            inspection.Syntax.Entries,
            entry => Assert.True(entry.Value.Length <= 4));
    }

    [Fact]
    public void RejectsUnsafeProjectionLimits()
    {
        var service = new XamlDocumentInspectionService();
        var source = SourceText.From("<Page/>");

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Inspect(
            source,
            options: new XamlDocumentInspectionOptions
            {
                MaximumProjectionEntries = 0
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Inspect(
            source,
            options: new XamlDocumentInspectionOptions
            {
                MaximumPreviewLength =
                    XamlDocumentInspectionOptions.MaximumSupportedPreviewLength + 1
            }));
    }
}
