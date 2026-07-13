// Removes redundant braces around single-statement embedded bodies of
// if/else/for/foreach/while/lock statements (RCS1002/RCS1004 equivalent),
// syntax-only. Conservative: skips blocks whose single statement is a
// declaration/label/local function, blocks carrying comments or
// preprocessor directives, and any unwrap that could rebind a trailing
// `else` (dangling-else hazard). Run csharpier afterwards.
//
// Usage: dotnet run --project scripts/RemoveRedundantBraces -c Release -- <root-dir>
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = args.Length > 0 ? args[0] : ".";
string[] excludes = ["/obj/", "/bin/", "/Generated/"];

int filesChanged = 0,
    blocksRemoved = 0;

foreach (
    var path in Directory
        .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(p => !excludes.Any(e => p.Replace('\\', '/').Contains(e)))
)
{
    var bytes = File.ReadAllBytes(path);
    bool hadBom = bytes is [0xEF, 0xBB, 0xBF, ..];
    var text = new UTF8Encoding(false).GetString(hadBom ? bytes.AsSpan(3) : bytes);
    var tree = CSharpSyntaxTree.ParseText(text);
    var rewriter = new BraceRemover();
    var newRoot = rewriter.Visit(tree.GetRoot());
    if (rewriter.Removed > 0)
    {
        File.WriteAllText(path, newRoot.ToFullString(), new UTF8Encoding(hadBom));
        filesChanged++;
        blocksRemoved += rewriter.Removed;
    }
}

Console.WriteLine($"removed braces from {blocksRemoved} block(s) in {filesChanged} file(s)");

class BraceRemover : CSharpSyntaxRewriter
{
    public int Removed { get; private set; }

    static bool BlockIsUnwrappable(BlockSyntax block)
    {
        if (block.Statements.Count != 1)
            return false;
        var stmt = block.Statements[0];
        if (
            stmt is LocalDeclarationStatementSyntax
            || stmt is LabeledStatementSyntax
            || stmt is LocalFunctionStatementSyntax
        )
            return false;
        // Keep blocks that carry comments or preprocessor directives anywhere.
        if (
            block
                .DescendantTrivia(descendIntoTrivia: true)
                .Any(t =>
                    t.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
                    || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    || t.IsDirective
                )
        )
            return false;
        return true;
    }

    // True if the embedded-statement spine of `stmt` ends in an if without
    // an else — unwrapping such a statement before an `else` rebinds it.
    static bool EndsInOpenIf(StatementSyntax stmt)
    {
        while (true)
            switch (stmt)
            {
                case IfStatementSyntax ifs when ifs.Else is null:
                    return true;
                case IfStatementSyntax ifs:
                    stmt = ifs.Else!.Statement;
                    break;
                case ForStatementSyntax f when f.Statement is not BlockSyntax:
                    stmt = f.Statement;
                    break;
                case ForEachStatementSyntax fe when fe.Statement is not BlockSyntax:
                    stmt = fe.Statement;
                    break;
                case WhileStatementSyntax w when w.Statement is not BlockSyntax:
                    stmt = w.Statement;
                    break;
                case LockStatementSyntax l when l.Statement is not BlockSyntax:
                    stmt = l.Statement;
                    break;
                case UsingStatementSyntax u when u.Statement is not BlockSyntax:
                    stmt = u.Statement;
                    break;
                default:
                    return false;
            }
    }

    StatementSyntax Unwrap(BlockSyntax block)
    {
        Removed++;
        var stmt = block.Statements[0];
        return stmt.WithLeadingTrivia(block.GetLeadingTrivia().AddRange(stmt.GetLeadingTrivia()))
            .WithTrailingTrivia(stmt.GetTrailingTrivia().AddRange(block.GetTrailingTrivia()));
    }

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        var visited = (IfStatementSyntax)base.VisitIfStatement(node)!;
        if (visited.Statement is BlockSyntax thenBlock && BlockIsUnwrappable(thenBlock))
        {
            var inner = thenBlock.Statements[0];
            // Unwrapping the then-branch of an if that has an else must not
            // let that else rebind to an inner open if.
            if (visited.Else is null || !EndsInOpenIf(inner))
                visited = visited.WithStatement(Unwrap(thenBlock));
        }
        if (
            visited.Else is { Statement: BlockSyntax elseBlock } el
            && BlockIsUnwrappable(elseBlock)
            && elseBlock.Statements[0] is not IfStatementSyntax
        )
            visited = visited.WithElse(el.WithStatement(Unwrap(elseBlock)));
        return visited;
    }

    // `hazard` must be computed from the ORIGINAL node (rewritten nodes are
    // detached and have no Parent chain to walk).
    SyntaxNode? VisitLoopLike<T>(
        T node,
        bool hazard,
        Func<T, StatementSyntax> body,
        Func<T, StatementSyntax, T> replace
    )
        where T : StatementSyntax
    {
        if (body(node) is BlockSyntax block && BlockIsUnwrappable(block))
        {
            var inner = block.Statements[0];
            // A loop body ending in an open `if` must stay braced when a
            // trailing else (from an enclosing if) could rebind to it.
            if (!EndsInOpenIf(inner) || !hazard)
                return replace(node, Unwrap(block));
        }
        return node;
    }

    static bool HasTrailingElseHazard(StatementSyntax node)
    {
        // If any ancestor chain of embedded statements places this statement
        // directly before an `else` keyword, unwrapping can rebind it.
        for (SyntaxNode? cur = node; cur is StatementSyntax s; cur = cur.Parent)
        {
            if (cur.Parent is IfStatementSyntax pif && pif.Statement == cur && pif.Else is not null)
                return true;
            if (cur.Parent is ElseClauseSyntax || cur.Parent is BlockSyntax)
                return false;
        }
        return false;
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        bool hazard = HasTrailingElseHazard(node);
        var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
        return VisitLoopLike(visited, hazard, n => n.Statement, (n, s) => n.WithStatement(s));
    }

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        bool hazard = HasTrailingElseHazard(node);
        var visited = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
        return VisitLoopLike(visited, hazard, n => n.Statement, (n, s) => n.WithStatement(s));
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        bool hazard = HasTrailingElseHazard(node);
        var visited = (ForStatementSyntax)base.VisitForStatement(node)!;
        return VisitLoopLike(visited, hazard, n => n.Statement, (n, s) => n.WithStatement(s));
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        bool hazard = HasTrailingElseHazard(node);
        var visited = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
        return VisitLoopLike(visited, hazard, n => n.Statement, (n, s) => n.WithStatement(s));
    }

    public override SyntaxNode? VisitLockStatement(LockStatementSyntax node)
    {
        bool hazard = HasTrailingElseHazard(node);
        var visited = (LockStatementSyntax)base.VisitLockStatement(node)!;
        return VisitLoopLike(visited, hazard, n => n.Statement, (n, s) => n.WithStatement(s));
    }
}
