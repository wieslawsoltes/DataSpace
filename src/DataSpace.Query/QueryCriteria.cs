using DataSpace.Core;

namespace DataSpace.Query;

/// <summary>Field-relative QBE criteria. AND binds within a row; separate design rows are OR alternatives.</summary>
public static class QueryCriteria
{
    public static string Compile(string expression, string criterion)
    {
        var field = SqlText.Format(SqlText.Parse(expression));
        if (criterion.Length > 16384) throw new DataSpaceException("A criterion is limited to 16,384 characters.");
        var expanded = Expand(field, criterion.Trim(), 0);
        return SqlText.Format(SqlText.Parse(expanded));
    }
    private static string Expand(string field, string text, int depth)
    {
        text = text.Trim();
        if (depth > 32 || text.Length == 0) throw new DataSpaceException("Invalid or excessively nested criterion.");
        var tokens = SqlLexer.Read(text).Where(t => t.Kind != TokenKind.End).ToList();
        if (tokens.Any(t => t.Kind == TokenKind.Symbol && t.Text == ";")) throw new DataSpaceException("A criterion cannot contain a SQL statement.");
        var level = 0; var between = false; var split = -1;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.Symbol && token.Text == "(") level++;
            if (token.Kind == TokenKind.Symbol && token.Text == ")") level--;
            if (level != 0 || token.Kind != TokenKind.Word) continue;
            if (Names.Equal(token.Text, "BETWEEN")) between = true;
            else if (Names.Equal(token.Text, "AND") && between) between = false;
            else if (Names.Equal(token.Text, "OR")) { split = index; break; }
            else if (Names.Equal(token.Text, "AND") && split < 0) split = index;
        }
        if (split >= 0)
        {
            var token = tokens[split];
            return "(" + Expand(field, text[..token.Position], depth + 1) + ") " + token.Text + " (" + Expand(field, text[(token.Position + token.Text.Length)..], depth + 1) + ")";
        }
        if (tokens.Count == 0) throw new DataSpaceException("The criterion is empty.");
        if (tokens[0].Text == "(" && tokens[^1].Text == ")" && Enclosed(tokens)) return Expand(field, text[1..^1].Trim(), depth + 1);
        if (tokens[0].Kind == TokenKind.Word && Names.Equal(tokens[0].Text, "NOT") && tokens.Count > 1 && tokens[1].Text == "(")
            return "NOT (" + Expand(field, text[3..].Trim(), depth + 1) + ")";
        var first = tokens[0];
        if (first.Kind == TokenKind.Symbol && new[] { "=", "!=", "<>", ">", "<", ">=", "<=" }.Contains(first.Text) ||
            first.Kind == TokenKind.Word && new[] { "LIKE", "IN", "BETWEEN", "IS", "NOT" }.Contains(first.Text.ToUpperInvariant()))
            return field + " " + text;
        // Access-style bare text is an equality literal, not an unbound identifier.
        if (tokens.All(t => t.Kind == TokenKind.Word) && !new[] { "TRUE", "FALSE", "NULL" }.Contains(text.ToUpperInvariant()))
            return field + " = '" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
        var value = SqlText.Parse(text);
        return value is BinaryExpr or NullExpr or InExpr ? text : field + " = (" + text + ")";
    }
    private static bool Enclosed(List<Token> tokens)
    {
        var level = 0;
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Kind == TokenKind.Symbol && tokens[i].Text == "(") level++;
            if (tokens[i].Kind == TokenKind.Symbol && tokens[i].Text == ")") level--;
            if (level == 0) return false;
        }
        return true;
    }
}

internal static class SqlText
{
    public static Expr Parse(string text)
    {
        var parser = new SqlParser(text); var expression = parser.Expression(); parser.End(); return expression;
    }
    public static string Format(Expr expression) => expression switch
    {
        LiteralExpr { Value: null } => "NULL",
        LiteralExpr { Value: string value } => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'",
        LiteralExpr { Value: bool value } => value ? "TRUE" : "FALSE",
        LiteralExpr { Value: DateTime value } => "#" + value.ToString("O", FieldValues.Culture) + "#",
        LiteralExpr value => FieldValues.FromObject(value.Value) ?? "NULL",
        NameExpr { Parameter: true } value => "@" + value.Name,
        NameExpr value => string.Join(".", value.Name.Split('.').Select(Names.Quote)),
        StarExpr => "*",
        UnaryExpr value => "(" + value.Op + " " + Format(value.Operand) + ")",
        BinaryExpr value => "(" + Format(value.Left) + " " + value.Op + " " + Format(value.Right) + ")",
        NullExpr value => "(" + Format(value.Operand) + " IS " + (value.Negated ? "NOT " : "") + "NULL)",
        InExpr value => "(" + Format(value.Operand) + (value.Negated ? " NOT IN (" : " IN (") + string.Join(", ", value.Items.Select(Format)) + "))",
        FunctionExpr value => value.Name + "(" + string.Join(", ", value.Arguments.Select(Format)) + ")",
        _ => throw new DataSpaceException("Expression cannot be represented in Design View.")
    };
    public static bool Same(Expr left, Expr right) => (left, right) switch
    {
        (LiteralExpr a, LiteralExpr b) => Equals(a.Value, b.Value),
        (NameExpr a, NameExpr b) => a.Parameter == b.Parameter && Names.Equal(a.Name, b.Name),
        (StarExpr, StarExpr) => true,
        (UnaryExpr a, UnaryExpr b) => a.Op == b.Op && Same(a.Operand, b.Operand),
        (BinaryExpr a, BinaryExpr b) => a.Op == b.Op && Same(a.Left, b.Left) && Same(a.Right, b.Right),
        (NullExpr a, NullExpr b) => a.Negated == b.Negated && Same(a.Operand, b.Operand),
        (InExpr a, InExpr b) => a.Negated == b.Negated && Same(a.Operand, b.Operand) && Sequence(a.Items, b.Items),
        (FunctionExpr a, FunctionExpr b) => a.Name == b.Name && Sequence(a.Arguments, b.Arguments),
        _ => false
    };
    private static bool Sequence(List<Expr> left, List<Expr> right) => left.Count == right.Count && left.Zip(right).All(p => Same(p.First, p.Second));
}
