using System.Text;
using Microsoft.CodeAnalysis.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Tooling;

namespace ProGPU.Samples;

public static class XamlPlaygroundPage
{
    private const string InitialSource = """
<Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <StackPanel Margin="24">
    <TextBlock Text="Hello from the XAML playground" />
  </StackPanel>
</Page>
""";

    public static FrameworkElement Create()
    {
        var root = new StackPanel { Margin = new Thickness(20), Orientation = Orientation.Vertical };
        root.Children.Add(new TextBlock { FontSize = 22, Text = "XAML Playground" });
        root.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 12),
            Text = "Edit source and inspect bounded projections of the same lossless syntax and schema-neutral infoset used by builds and the CLI."
        });
        var editor = new TextBox
        {
            Text = InitialSource,
            AcceptsReturn = true,
            Height = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var status = new TextBlock { Margin = new Thickness(0, 12, 0, 0), Text = "Ready." };
        var syntaxOutput = CreateOutput("Syntax details will appear here.");
        var tokenOutput = CreateOutput("Lossless tokens will appear here.");
        var infosetOutput = CreateOutput("Infoset details will appear here.");
        var diagnosticsOutput = CreateOutput("Diagnostics will appear here.");
        var views = new Pivot { Margin = new Thickness(0, 8, 0, 0), Height = 300 };
        views.Items.Add(new PivotItem("Syntax", syntaxOutput));
        views.Items.Add(new PivotItem("Tokens", tokenOutput));
        views.Items.Add(new PivotItem("Infoset", infosetOutput));
        views.Items.Add(new PivotItem("Diagnostics", diagnosticsOutput));
        var inspect = new Button { Margin = new Thickness(0, 12, 0, 0), Content = "Parse and inspect" };
        inspect.Click += (_, _) =>
        {
            var source = SourceText.From(editor.Text);
            var inspection = new XamlDocumentInspectionService().Inspect(
                source,
                "Playground.xaml",
                new XamlDocumentInspectionOptions
                {
                    ParseOptions = new XamlParseOptions
                    {
                        Mode = XamlParseMode.Recovering
                    }
                });
            var statistics = inspection.Statistics;
            status.Text =
                $"Root: {inspection.SyntaxTree.GetRoot()?.QualifiedName ?? "<none>"}; " +
                $"tokens: {statistics.Tokens}; syntax objects: {statistics.SyntaxObjects}; " +
                $"infoset objects: {statistics.InfosetObjects}; errors: {statistics.Errors}.";
            syntaxOutput.Text = Render(inspection.Syntax);
            tokenOutput.Text = Render(inspection.Tokens);
            infosetOutput.Text = Render(inspection.InfosetProjection);
            diagnosticsOutput.Text = inspection.Diagnostics.TotalEntryCount == 0
                ? "No diagnostics."
                : Render(inspection.Diagnostics);
        };
        root.Children.Add(editor);
        root.Children.Add(inspect);
        root.Children.Add(status);
        root.Children.Add(views);
        return root;
    }

    private static TextBox CreateOutput(string text) => new TextBox
    {
        AcceptsReturn = true,
        Height = 250,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Text = text
    };

    private static string Render(XamlInspectionProjection projection)
    {
        var builder = new StringBuilder();
        foreach (var entry in projection.Entries)
        {
            builder.Append(' ', entry.Depth * 2);
            builder.Append(entry.Kind);
            builder.Append(' ');
            builder.Append(entry.Name);
            if (entry.Value.Length != 0)
                builder.Append(" = ").Append(entry.Value);
            builder.Append(" [").Append(entry.SourceSpan.Start)
                .Append("..").Append(entry.SourceSpan.End).Append(']');
            if (entry.HasStableId)
                builder.Append(" #").Append(entry.StableId.ToString("x16"));
            builder.AppendLine();
        }
        if (projection.IsTruncated)
            builder.Append("… ").Append(projection.TotalEntryCount - projection.Entries.Length)
                .AppendLine(" more entries omitted by the inspection bound.");
        return builder.ToString();
    }
}
