using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Editing;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Workspaces;

/// <summary>
/// Applies XAML operations to immutable Roslyn Solution snapshots. It never writes files or
/// mutates a Workspace; callers decide whether and how to commit the returned solution.
/// </summary>
public sealed class XamlAdditionalDocumentService
{
    public async Task<XamlSyntaxTree> ParseAsync(
        Solution solution,
        DocumentId additionalDocumentId,
        XamlParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var document = GetRequiredDocument(solution, additionalDocumentId);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        return XamlParser.Parse(text, document.FilePath ?? document.Name, options, cancellationToken);
    }

    public async Task<XamlWorkspaceEditResult> EditAsync(
        Solution solution,
        DocumentId additionalDocumentId,
        Action<XamlSyntaxEditor> edit,
        XamlParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (edit == null) throw new ArgumentNullException(nameof(edit));
        var document = GetRequiredDocument(solution, additionalDocumentId);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var tree = XamlParser.Parse(text, document.FilePath ?? document.Name, options, cancellationToken);
        var editor = new XamlSyntaxEditor(tree);
        edit(editor);
        var changes = editor.GetTextChanges();
        if (changes.Length == 0) return new XamlWorkspaceEditResult(solution, tree, changes);
        var changedText = text.WithChanges(changes);
        var changedSolution = solution.WithAdditionalDocumentText(additionalDocumentId, changedText);
        var changedTree = tree.WithChangedText(changedText, cancellationToken);
        return new XamlWorkspaceEditResult(changedSolution, changedTree, changes);
    }

    private static TextDocument GetRequiredDocument(Solution solution, DocumentId id)
    {
        if (solution == null) throw new ArgumentNullException(nameof(solution));
        if (id == null) throw new ArgumentNullException(nameof(id));
        return solution.GetAdditionalDocument(id) ??
            throw new ArgumentException("The supplied ID is not an AdditionalDocument in this solution.", nameof(id));
    }
}

public sealed class XamlWorkspaceEditResult
{
    internal XamlWorkspaceEditResult(
        Solution solution,
        XamlSyntaxTree syntaxTree,
        ImmutableArray<TextChange> changes)
    {
        Solution = solution;
        SyntaxTree = syntaxTree;
        Changes = changes;
    }

    public Solution Solution { get; }
    public XamlSyntaxTree SyntaxTree { get; }
    public ImmutableArray<TextChange> Changes { get; }
}
