using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Editing;

/// <summary>
/// Batches non-overlapping source edits against one immutable XAML tree and reparses once.
/// The service deliberately returns Roslyn text primitives so workspace hosts can compose it.
/// </summary>
public sealed class XamlSyntaxEditor
{
    private readonly XamlSyntaxTree _tree;
    private readonly List<TextChange> _changes = new List<TextChange>();

    public XamlSyntaxEditor(XamlSyntaxTree tree) =>
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));

    public void ReplaceNode(XamlSyntaxNode node, string replacement)
    {
        ValidateNode(node);
        AddChange(new TextChange(node.FullSpan, replacement ?? string.Empty));
    }

    public void RemoveNode(XamlSyntaxNode node) => ReplaceNode(node, string.Empty);

    public void InsertBefore(XamlSyntaxNode node, string text)
    {
        ValidateNode(node);
        AddChange(new TextChange(new TextSpan(node.FullSpan.Start, 0), text ?? string.Empty));
    }

    public void InsertAfter(XamlSyntaxNode node, string text)
    {
        ValidateNode(node);
        AddChange(new TextChange(new TextSpan(node.FullSpan.End, 0), text ?? string.Empty));
    }

    public void ReplaceSpan(TextSpan span, string replacement)
    {
        if (span.Start < 0 || span.End > _tree.GetText().Length)
            throw new ArgumentOutOfRangeException(nameof(span));
        AddChange(new TextChange(span, replacement ?? string.Empty));
    }

    public ImmutableArray<TextChange> GetTextChanges() =>
        _changes.OrderBy(change => change.Span.Start).ToImmutableArray();

    public SourceText GetChangedText() => _tree.GetText().WithChanges(GetTextChanges());

    public XamlSyntaxTree GetChangedTree() => _tree.WithChangedText(GetChangedText());

    private void AddChange(TextChange change)
    {
        foreach (var existing in _changes)
        {
            if (Overlaps(existing.Span, change.Span))
                throw new InvalidOperationException("XAML editor changes cannot overlap.");
        }
        _changes.Add(change);
    }

    private void ValidateNode(XamlSyntaxNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (!ReferenceEquals(node.SyntaxTree, _tree))
            throw new ArgumentException("The node belongs to a different XAML syntax tree.", nameof(node));
    }

    private static bool Overlaps(TextSpan left, TextSpan right)
    {
        if (left.Length == 0 && right.Length == 0) return left.Start == right.Start;
        if (left.Length == 0) return right.Contains(left.Start);
        if (right.Length == 0) return left.Contains(right.Start);
        return left.OverlapsWith(right);
    }
}
