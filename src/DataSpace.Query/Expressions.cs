using System.Globalization;
using System.Text.RegularExpressions;
using DataSpace.Core;

namespace DataSpace.Query;

internal sealed class EvaluationContext
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Ambiguous { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public List<EvaluationContext>? Group { get; set; }
    public EvaluationContext Clone()
    {
        var context = new EvaluationContext { Parameters = Parameters, Group = Group };
        foreach (var pair in Values) context.Values[pair.Key] = pair.Value;
        context.Ambiguous.UnionWith(Ambiguous);
        return context;
    }
    public object? Resolve(string name, bool parameter = false)
    {
        if (parameter) return Parameters.TryGetValue(name, out var p) ? p : throw new DataSpaceException($"Parameter '@{name}' requires a value.");
        if (Ambiguous.Contains(name)) throw new DataSpaceException($"Field '{name}' is ambiguous. Qualify it with a table alias.");
        if (Values.TryGetValue(name, out var value)) return value;
        if (Parameters.TryGetValue(name, out value)) return value;
        throw new DataSpaceException($"Unknown field or parameter '{name}'.");
    }
}

internal abstract record Expr
{
    public abstract object? Eval(EvaluationContext context);
    public virtual bool Aggregate => false;
    public virtual bool GroupSafe(IReadOnlyList<Expr> groups) => groups.Any(group => SqlText.Same(group, this));
}
internal sealed record LiteralExpr(object? Value) : Expr
{
    public override object? Eval(EvaluationContext context) => Value;
    public override bool GroupSafe(IReadOnlyList<Expr> groups) => true;
}
internal sealed record NameExpr(string Name, bool Parameter = false) : Expr
{
    public override object? Eval(EvaluationContext context) => context.Resolve(Name, Parameter);
    public override bool GroupSafe(IReadOnlyList<Expr> groups) => Parameter || groups.OfType<NameExpr>().Any(g => Names.Equal(g.Name, Name));
}
internal sealed record StarExpr : Expr { public override object? Eval(EvaluationContext context) => 1m; }
internal sealed record UnaryExpr(string Op, Expr Operand) : Expr
{
    public override bool Aggregate => Operand.Aggregate;
    public override bool GroupSafe(IReadOnlyList<Expr> groups) => groups.Any(group => SqlText.Same(group, this)) || Operand.GroupSafe(groups);
    public override object? Eval(EvaluationContext context)
    {
        var value = Operand.Eval(context);
        if (value is null) return null;
        return Op switch { "NOT" => !SqlValue.Truth(value), "-" => -SqlValue.Number(value), "+" => SqlValue.Number(value), _ => throw new DataSpaceException("Invalid unary operator.") };
    }
}
internal sealed record BinaryExpr(string Op, Expr Left, Expr Right) : Expr
{
    public override bool Aggregate => Left.Aggregate || Right.Aggregate;
    public override bool GroupSafe(IReadOnlyList<Expr> groups) => groups.Any(group => SqlText.Same(group, this)) || Left.GroupSafe(groups) && Right.GroupSafe(groups);
    public override object? Eval(EvaluationContext context)
    {
        var a = Left.Eval(context);
        if (Op == "AND" && a is not null && !SqlValue.Truth(a)) return false;
        if (Op == "OR" && a is not null && SqlValue.Truth(a)) return true;
        var b = Right.Eval(context);
        if (Op == "AND") return b is not null && !SqlValue.Truth(b) ? false : a is null || b is null ? null : true;
        if (Op == "OR") return b is not null && SqlValue.Truth(b) ? true : a is null || b is null ? null : false;
        if (Op == "&") return SqlValue.Text(a) + SqlValue.Text(b);
        if (a is null || b is null) return null;
        return Op switch
        {
            "+" => SqlValue.Number(a) + SqlValue.Number(b),
            "-" => SqlValue.Number(a) - SqlValue.Number(b),
            "*" => SqlValue.Number(a) * SqlValue.Number(b),
            "/" => SqlValue.Number(b) == 0 ? throw new DataSpaceException("Division by zero.") : SqlValue.Number(a) / SqlValue.Number(b),
            "%" => SqlValue.Number(b) == 0 ? throw new DataSpaceException("Division by zero.") : SqlValue.Number(a) % SqlValue.Number(b),
            "=" => SqlValue.Compare(a, b) == 0,
            "<>" or "!=" => SqlValue.Compare(a, b) != 0,
            "<" => SqlValue.Compare(a, b) < 0,
            ">" => SqlValue.Compare(a, b) > 0,
            "<=" => SqlValue.Compare(a, b) <= 0,
            ">=" => SqlValue.Compare(a, b) >= 0,
            "LIKE" => SqlValue.Like(SqlValue.Text(a), SqlValue.Text(b)),
            _ => throw new DataSpaceException($"Unknown operator {Op}.")
        };
    }
}
internal sealed record NullExpr(Expr Operand, bool Negated) : Expr
{
    public override bool Aggregate => Operand.Aggregate;
    public override bool GroupSafe(IReadOnlyList<Expr> groups) => Operand.GroupSafe(groups);
    public override object? Eval(EvaluationContext context) => (Operand.Eval(context) is null) != Negated;
}
internal sealed record InExpr(Expr Operand, List<Expr> Items, bool Negated) : Expr
{
    public override bool Aggregate => Operand.Aggregate || Items.Any(i => i.Aggregate);
    public override bool GroupSafe(IReadOnlyList<Expr> groups) => Operand.GroupSafe(groups) && Items.All(i => i.GroupSafe(groups));
    public override object? Eval(EvaluationContext context)
    {
        var value = Operand.Eval(context);
        if (value is null) return null;
        var hasNull = false;
        foreach (var expression in Items)
        {
            var item = expression.Eval(context);
            if (item is null) hasNull = true;
            else if (SqlValue.Compare(value, item) == 0) return !Negated;
        }
        return hasNull ? null : Negated;
    }
}
internal sealed record FunctionExpr(string Name, List<Expr> Arguments) : Expr
{
    private bool IsAggregate => Name is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" or "FIRST" or "LAST";
    public override bool Aggregate => IsAggregate || Arguments.Any(a => a.Aggregate);
    public override bool GroupSafe(IReadOnlyList<Expr> groups) => IsAggregate || groups.Any(group => SqlText.Same(group, this)) || Arguments.All(a => a.GroupSafe(groups));
    public override object? Eval(EvaluationContext context)
    {
        void Arity(int min, int max)
        { if (Arguments.Count < min || Arguments.Count > max) throw new DataSpaceException($"{Name} expects {min}" + (min == max ? "" : $"–{max}") + " argument(s)."); }
        if (IsAggregate)
        {
            Arity(1, 1);
            if (Arguments[0].Aggregate) throw new DataSpaceException("Nested aggregate functions are not supported.");
            var group = context.Group ?? throw new DataSpaceException("Aggregate function is not valid in this context.");
            var values = group.Select(Arguments[0].Eval).Where(v => v is not null).ToArray();
            if (Name == "COUNT") return (long)values.Length;
            if (values.Length == 0) return null;
            return Name switch
            {
                "SUM" => values.Sum(SqlValue.Number), "AVG" => values.Average(SqlValue.Number),
                "MIN" => values.MinBy(v => v, SqlValue.Comparer), "MAX" => values.MaxBy(v => v, SqlValue.Comparer),
                "FIRST" => values[0], "LAST" => values[^1], _ => null
            };
        }
        object? A(int i) => Arguments[i].Eval(context);
        if (Name == "IIF") { Arity(3, 3); return A(0) is { } predicate && SqlValue.Truth(predicate) ? A(1) : A(2); }
        if (Name is "NZ" or "COALESCE")
        { Arity(1, 16); foreach (var arg in Arguments) if (arg.Eval(context) is { } v) return v; return Name == "NZ" && Arguments.Count == 1 ? "" : null; }
        if (Name is "DATE" or "NOW") { Arity(0, 0); return Name == "DATE" ? DateTime.Today : DateTime.Now; }
        if (Name == "ISNULL") { Arity(1, 1); return A(0) is null; }
        if (Name is "LEN" or "LCASE" or "UCASE" or "LOWER" or "UPPER" or "TRIM" or "LTRIM" or "RTRIM" or "ABS" or "INT" or "CSTR" or "CINT" or "CDBL" or "CDEC" or "YEAR" or "MONTH" or "DAY")
        {
            Arity(1, 1); var value = A(0); if (value is null) return null;
            return Name switch
            {
                "LEN" => (long)SqlValue.Text(value).Length, "LCASE" or "LOWER" => SqlValue.Text(value).ToLowerInvariant(),
                "UCASE" or "UPPER" => SqlValue.Text(value).ToUpperInvariant(), "TRIM" => SqlValue.Text(value).Trim(),
                "LTRIM" => SqlValue.Text(value).TrimStart(), "RTRIM" => SqlValue.Text(value).TrimEnd(),
                "ABS" => Math.Abs(SqlValue.Number(value)), "INT" => decimal.Floor(SqlValue.Number(value)),
                "CSTR" => SqlValue.Text(value), "CINT" => decimal.Round(SqlValue.Number(value), 0),
                "CDBL" or "CDEC" => SqlValue.Number(value), "YEAR" => (long)SqlValue.Date(value).Year,
                "MONTH" => (long)SqlValue.Date(value).Month, "DAY" => (long)SqlValue.Date(value).Day, _ => null
            };
        }
        if (Name == "ROUND") { Arity(1, 2); return A(0) is { } number ? decimal.Round(SqlValue.Number(number), Arguments.Count == 2 ? checked((int)SqlValue.Number(A(1))) : 0) : null; }
        if (Name is "LEFT" or "RIGHT")
        {
            Arity(2, 2); if (A(0) is not { } value || A(1) is null) return null;
            var text = SqlValue.Text(value); var count = Math.Clamp(checked((int)SqlValue.Number(A(1))), 0, text.Length);
            return Name == "LEFT" ? text[..count] : text[(text.Length - count)..];
        }
        if (Name is "MID" or "SUBSTRING")
        {
            Arity(2, 3); if (A(0) is not { } value) return null;
            var text = SqlValue.Text(value); var start = Math.Clamp(checked((int)SqlValue.Number(A(1))) - 1, 0, text.Length);
            var count = Arguments.Count == 3 ? Math.Clamp(checked((int)SqlValue.Number(A(2))), 0, text.Length - start) : text.Length - start;
            return text.Substring(start, count);
        }
        if (Name == "INSTR") { Arity(2, 2); return A(0) is null || A(1) is null ? null : (long)(SqlValue.Text(A(0)).IndexOf(SqlValue.Text(A(1)), StringComparison.OrdinalIgnoreCase) + 1); }
        if (Name == "REPLACE") { Arity(3, 3); return A(0) is null ? null : SqlValue.Text(A(0)).Replace(SqlValue.Text(A(1)), SqlValue.Text(A(2)), StringComparison.OrdinalIgnoreCase); }
        if (Name == "DATEADD")
        {
            Arity(3, 3); if (A(1) is null || A(2) is null) return null;
            var date = SqlValue.Date(A(2)); var n = checked((int)SqlValue.Number(A(1)));
            return SqlValue.Text(A(0)).ToLowerInvariant() switch
            { "yyyy" => date.AddYears(n), "q" => date.AddMonths(checked(n * 3)), "m" => date.AddMonths(n), "d" or "y" => date.AddDays(n), "ww" => date.AddDays(n * 7), "h" => date.AddHours(n), "n" => date.AddMinutes(n), "s" => date.AddSeconds(n), _ => throw new DataSpaceException("Unsupported DateAdd interval.") };
        }
        if (Name == "FORMAT") { Arity(2, 2); return A(0) is IFormattable value ? value.ToString(SqlValue.Text(A(1)), CultureInfo.InvariantCulture) : SqlValue.Text(A(0)); }
        throw new DataSpaceException($"Function '{Name}' is not supported.");
    }
}

internal static class SqlValue
{
    public static readonly IComparer<object?> Comparer = System.Collections.Generic.Comparer<object?>.Create(Compare);
    public static bool Truth(object? value) => value switch { null => false, bool b => b, string s => !string.IsNullOrEmpty(s) && s != "0" && !Names.Equal(s, "False"), _ => Number(value) != 0 };
    public static string Text(object? value) => FieldValues.FromObject(value) ?? "";
    public static decimal Number(object? value)
    {
        try { return value is bool boolean ? (boolean ? -1m : 0m) : Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException) { throw new DataSpaceException($"'{Text(value)}' is not numeric."); }
    }
    public static DateTime Date(object? value) => value is DateTime date ? date : DateTime.Parse(Text(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public static int Compare(object? a, object? b)
    {
        if (a is null) return b is null ? 0 : -1;
        if (b is null) return 1;
        if (a is DateTime || b is DateTime) return Date(a).CompareTo(Date(b));
        if (a is decimal or long or int or double or bool || b is decimal or long or int or double or bool) return Number(a).CompareTo(Number(b));
        return StringComparer.OrdinalIgnoreCase.Compare(Text(a), Text(b));
    }
    public static string Key(IEnumerable<object?> values) => FieldValues.Key(values.Select(v => v switch
    { null => null, decimal or long or int or bool => "N" + Number(v).ToString("G29", CultureInfo.InvariantCulture), DateTime date => "D" + date.Ticks, _ => "S" + Text(v).ToUpperInvariant() }));
    public static bool Like(string text, string pattern)
    {
        var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("%", ".*").Replace("\\?", ".").Replace("_", ".") + "$";
        return Regex.IsMatch(text, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    }
}

/// <summary>Evaluates a single, parsed Access-style expression without executing code or SQL statements.</summary>
public static class ExpressionEvaluator
{
    public static object? Evaluate(string expression, IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        var parser = new SqlParser(expression);
        var parsed = parser.Expression(); parser.End();
        var context = new EvaluationContext { Parameters = parameters ?? new Dictionary<string, object?>() };
        foreach (var pair in values) context.Values[pair.Key] = pair.Value;
        return parsed.Eval(context);
    }
}
