using System.Globalization;
using DataSpace.Core;

namespace DataSpace.Query;

internal abstract record Statement;
internal sealed record Source(string Table, string Alias);
internal sealed record Projection(Expr Expression, string? Alias = null, string? Wildcard = null);
internal sealed record Join(string Kind, Source Source, Expr? Condition);
internal sealed record Ordering(Expr Expression, bool Descending);
internal sealed record SelectStatement(List<Projection> Projections, Source? Source, List<Join> Joins, Expr? Where, List<Expr> Groups, Expr? Having, List<Ordering> Order, bool Distinct, int? Limit, int Offset) : Statement
{ public string? Into { get; init; } }
internal sealed record UnionStatement(List<SelectStatement> Queries, List<bool> All, List<Ordering> Order) : Statement;
internal sealed record InsertStatement(string Table, List<string> Fields, List<List<Expr>> Rows) : Statement;
internal sealed record InsertSelectStatement(string Table, List<string> Fields, Statement Query) : Statement;
internal sealed record UpdateStatement(string Table, List<(string Field, Expr Value)> Assignments, Expr? Where) : Statement;
internal sealed record DeleteStatement(string Table, Expr? Where) : Statement;
internal sealed record CreateStatement(TableDefinition Table) : Statement;
internal sealed record AlterStatement(string Table, FieldDefinition? Add, string? Drop) : Statement;
internal sealed record DropStatement(string Table) : Statement;

internal sealed partial class SqlParser
{
    private readonly List<Token> _tokens;
    private readonly string _sql;
    private int _index, _depth;
    private Token Current => _tokens[_index];
    public SqlParser(string sql) { _sql = sql; _tokens = SqlLexer.Read(sql); }
    private bool Is(string value) => (Current.Kind is TokenKind.Word or TokenKind.Symbol) && Names.Equal(Current.Text, value);
    private bool Match(string value) { if (!Is(value)) return false; _index++; return true; }
    private void Expect(string value) { if (!Match(value)) throw Error($"Expected '{value}', found '{Current.Text}'"); }
    private DataSpaceException Error(string message) => SqlLexer.Error(message, Current.Position);
    private string Identifier()
    {
        if (Current.Kind is not (TokenKind.Word or TokenKind.QuotedName)) throw Error("Expected a field or table name");
        return _tokens[_index++].Text;
    }
    public void End() { Match(";"); if (Current.Kind != TokenKind.End) throw Error("Unexpected token; only one SQL statement is allowed"); }
    public Statement Parse()
    {
        Statement statement;
        if (Match("SELECT")) statement = SelectOrMakeTable();
        else if (Match("TRANSFORM")) statement = Transform();
        else if (Match("TABLE")) statement = ReadQuery(TableSelect());
        else if (Match("INSERT")) statement = Insert();
        else if (Match("UPDATE")) statement = Update();
        else if (Match("DELETE")) { Match("*"); Expect("FROM"); var table = Identifier(); statement = new DeleteStatement(table, Match("WHERE") ? Expression() : null); }
        else if (Match("CREATE")) statement = Create();
        else if (Match("ALTER"))
        {
            Expect("TABLE"); var table = Identifier();
            if (Match("ADD")) { Match("COLUMN"); statement = new AlterStatement(table, Field(), null); }
            else { Expect("DROP"); Match("COLUMN"); statement = new AlterStatement(table, null, Identifier()); }
        }
        else if (Match("DROP")) statement = Drop();
        else throw Error("Expected SELECT, TRANSFORM, TABLE, INSERT, UPDATE, DELETE, CREATE, ALTER or DROP");
        End(); return statement;
    }
    private Statement ReadQuery(SelectStatement first)
    {
        if (first.Into is not null) throw Error("INTO cannot be nested in a read-only query");
        if (!Is("UNION")) return first;
        var queries = new List<SelectStatement> { first }; var all = new List<bool>();
        while (Match("UNION"))
        {
            if (queries.Count >= 32) throw Error("At most 32 UNION branches are supported");
            if (queries[^1].Order.Count > 0) throw Error("ORDER BY must follow the final UNION branch");
            all.Add(Match("ALL"));
            if (Match("SELECT")) queries.Add(Select());
            else { Expect("TABLE"); queries.Add(TableSelect()); }
        }
        if (queries.Any(q => q.Into is not null)) throw Error("INTO cannot be nested in UNION");
        var order = queries[^1].Order;
        queries[^1] = queries[^1] with { Order = [] };
        return new UnionStatement(queries, all, order);
    }
    private SelectStatement TableSelect()
    {
        var name = Identifier(); var order = Order();
        return new([new(new StarExpr(), Wildcard: "")], new(name, name), [], null, [], null, order, false, null, 0);
    }
    private static readonly HashSet<string> ClauseWords = new(StringComparer.OrdinalIgnoreCase)
        { "INTO", "PIVOT", "FROM", "WHERE", "GROUP", "HAVING", "ORDER", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "JOIN", "ON", "LIMIT", "OFFSET", "UNION", "ASC", "DESC", "SET", "VALUES" };
    private string? Alias()
    {
        if (Match("AS")) return Identifier();
        return Current.Kind == TokenKind.QuotedName || Current.Kind == TokenKind.Word && !ClauseWords.Contains(Current.Text) ? Identifier() : null;
    }
    private Source Source() { var table = Identifier(); return new(table, Alias() ?? table); }
    private List<Ordering> Order()
    {
        var order = new List<Ordering>();
        if (Match("ORDER"))
        {
            Expect("BY");
            do { var expression = Expression(); var descending = Match("DESC"); if (!descending) Match("ASC"); order.Add(new(expression, descending)); } while (Match(","));
        }
        return order;
    }
    private SelectStatement Select()
    {
        var distinct = Match("DISTINCT"); int? limit = Match("TOP") ? PositiveInteger() : null;
        var projections = new List<Projection>();
        do
        {
            if (Match("*")) projections.Add(new(new StarExpr(), Wildcard: ""));
            else if (Current.Kind is TokenKind.Word or TokenKind.QuotedName && _index + 2 < _tokens.Count && _tokens[_index + 1].Text == "." && _tokens[_index + 2].Text == "*")
            { var source = Identifier(); Expect("."); Expect("*"); projections.Add(new(new StarExpr(), Wildcard: source)); }
            else { var expression = Expression(); projections.Add(new(expression, Alias())); }
        } while (Match(","));
        var into = Match("INTO") ? Identifier() : null;
        Source? sourceTable = null; var joins = new List<Join>();
        if (Match("FROM"))
        {
            sourceTable = Source();
            while (Is("INNER") || Is("LEFT") || Is("JOIN") || Is("CROSS") || Is(","))
            {
                var kind = "INNER";
                if (Match(",")) { joins.Add(new("CROSS", Source(), null)); continue; }
                if (Match("LEFT")) { kind = "LEFT"; Match("OUTER"); }
                else if (Match("CROSS")) kind = "CROSS";
                else Match("INNER");
                Expect("JOIN"); var joined = Source(); Expr? condition = null;
                if (kind != "CROSS") { Expect("ON"); condition = Expression(); }
                joins.Add(new(kind, joined, condition));
            }
        }
        var where = Match("WHERE") ? Expression() : null;
        var groups = new List<Expr>();
        if (Match("GROUP")) { Expect("BY"); do groups.Add(Expression()); while (Match(",")); }
        var having = Match("HAVING") ? Expression() : null; var order = Order();
        if (Match("LIMIT")) limit = PositiveInteger();
        var offset = Match("OFFSET") ? PositiveInteger() : 0;
        return new(projections, sourceTable, joins, where, groups, having, order, distinct, limit, offset) { Into = into };
    }
    private SubqueryExpr ReadSubquery(SubqueryKind kind, Expr? operand = null, string comparison = "=", bool negated = false)
    {
        var start = Current.Position;
        Expect("SELECT");
        var query = ReadQuery(Select()); // Reject SELECT INTO and all nested action statements.
        var end = Current.Position; Expect(")");
        return new(query, _sql[start..end].Trim(), kind, operand, comparison, negated);
    }
    private int PositiveInteger()
    {
        if (Current.Kind != TokenKind.Number || !int.TryParse(Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0) throw Error("Expected a non-negative integer");
        _index++; return value;
    }
    private Statement Insert()
    {
        Expect("INTO"); var table = Identifier(); var fields = new List<string>();
        if (Match("(")) { do fields.Add(Identifier()); while (Match(",")); Expect(")"); }
        if (Match("SELECT")) return new InsertSelectStatement(table, fields, ReadQuery(Select()));
        Expect("VALUES"); var rows = new List<List<Expr>>();
        do { Expect("("); var row = new List<Expr>(); do row.Add(Expression()); while (Match(",")); Expect(")"); rows.Add(row); } while (Match(","));
        return new InsertStatement(table, fields, rows);
    }
    private UpdateStatement Update()
    {
        var table = Identifier(); Expect("SET"); var assignments = new List<(string, Expr)>();
        do { var field = Identifier(); Expect("="); assignments.Add((field, Expression())); } while (Match(","));
        return new(table, assignments, Match("WHERE") ? Expression() : null);
    }
    private FieldDefinition Field()
    {
        var name = Identifier(); var typeName = Identifier().ToUpperInvariant();
        var type = typeName switch
        {
            "COUNTER" or "AUTOINCREMENT" or "AUTONUMBER" => FieldType.AutoNumber,
            "INT" or "INTEGER" or "LONG" or "SHORT" or "BIGINT" => FieldType.Integer,
            "DECIMAL" or "NUMERIC" or "DOUBLE" or "SINGLE" or "FLOAT" => FieldType.Decimal,
            "CURRENCY" or "MONEY" => FieldType.Currency, "DATETIME" or "DATE" => FieldType.DateTime,
            "BIT" or "YESNO" or "BOOLEAN" => FieldType.YesNo, "MEMO" or "LONGTEXT" => FieldType.LongText,
            "TEXT" or "VARCHAR" or "CHAR" or "SHORTTEXT" => FieldType.ShortText,
            "GUID" or "UNIQUEIDENTIFIER" => FieldType.Guid, _ => throw Error($"Unsupported data type '{typeName}'")
        };
        var field = new FieldDefinition { Name = name, Type = type };
        if (Match("(")) { field.MaxLength = PositiveInteger(); Expect(")"); }
        while (true)
        {
            if (Match("PRIMARY")) { Expect("KEY"); field.PrimaryKey = true; }
            else if (Match("NOT")) { Expect("NULL"); field.Required = true; }
            else if (Match("UNIQUE")) field.Unique = true;
            else if (Match("DEFAULT")) field.DefaultValue = FieldValues.FromObject(Expression(4).Eval(new EvaluationContext()));
            else break;
        }
        return field;
    }
    public Expr Expression(int minimum = 0)
    {
        if (++_depth > 64) throw Error("Expression nesting exceeds 64 levels");
        try
        {
            Expr left;
            if (Match("NOT")) left = new UnaryExpr("NOT", Expression(3));
            else if (Match("-")) left = new UnaryExpr("-", Expression(7));
            else if (Match("+")) left = new UnaryExpr("+", Expression(7));
            else if (Match("EXISTS")) { Expect("("); left = ReadSubquery(SubqueryKind.Exists); }
            else if (Match("("))
            {
                if (Is("SELECT")) left = ReadSubquery(SubqueryKind.Scalar);
                else { left = Expression(); Expect(")"); }
            }
            else if (Match("NULL")) left = new LiteralExpr(null);
            else if (Match("TRUE")) left = new LiteralExpr(true);
            else if (Match("FALSE")) left = new LiteralExpr(false);
            else if (Current.Kind == TokenKind.Number)
            {
                if (!decimal.TryParse(Current.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw Error("Invalid number");
                _index++; left = new LiteralExpr(number);
            }
            else if (Current.Kind == TokenKind.Date)
            {
                if (!DateTime.TryParse(Current.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw Error("Invalid date literal");
                _index++; left = new LiteralExpr(date);
            }
            else if (Current.Kind == TokenKind.String) left = new LiteralExpr(_tokens[_index++].Text);
            else if (Current.Kind == TokenKind.Parameter) left = new NameExpr(_tokens[_index++].Text, true);
            else if (Current.Kind is TokenKind.Word or TokenKind.QuotedName)
            {
                var name = Identifier();
                if (Match("("))
                {
                    var args = new List<Expr>();
                    if (!Match(")")) { do args.Add(Match("*") ? new StarExpr() : Expression()); while (Match(",")); Expect(")"); }
                    left = new FunctionExpr(name.ToUpperInvariant(), args);
                }
                else { if (Match(".")) name += "." + Identifier(); left = new NameExpr(name); }
            }
            else throw Error("Expected an expression");
            while (true)
            {
                var op = Current.Text.ToUpperInvariant();
                var precedence = Current.Kind is not (TokenKind.Word or TokenKind.Symbol) ? -1 : op switch
                { "OR" => 1, "AND" => 2, "=" or "<>" or "!=" or "<" or ">" or "<=" or ">=" or "IS" or "LIKE" or "IN" or "BETWEEN" or "NOT" => 3, "&" => 4, "+" or "-" => 5, "*" or "/" or "%" => 6, _ => -1 };
                if (precedence < minimum) break;
                _index++; var negated = op == "NOT";
                if (negated) { op = Current.Text.ToUpperInvariant(); if (!Match("LIKE") && !Match("IN") && !Match("BETWEEN")) throw Error("Expected LIKE, IN or BETWEEN after NOT"); }
                if (op == "IS") { var not = Match("NOT"); Expect("NULL"); left = new NullExpr(left, not); }
                else if (op == "IN")
                {
                    Expect("(");
                    if (Is("SELECT")) left = ReadSubquery(SubqueryKind.In, left, negated: negated);
                    else { var items = new List<Expr>(); do items.Add(Expression()); while (Match(",")); Expect(")"); left = new InExpr(left, items, negated); }
                }
                else if (op == "BETWEEN")
                {
                    var lower = Expression(4); Expect("AND"); var upper = Expression(4);
                    left = new BinaryExpr("AND", new BinaryExpr(">=", left, lower), new BinaryExpr("<=", left, upper));
                    if (negated) left = new UnaryExpr("NOT", left);
                }
                else if (op is "=" or "<>" or "!=" or "<" or ">" or "<=" or ">=" && (Is("ANY") || Is("SOME") || Is("ALL")))
                {
                    var kind = Match("ALL") ? SubqueryKind.All : SubqueryKind.Any;
                    if (kind == SubqueryKind.Any) { if (!Match("ANY")) Expect("SOME"); }
                    Expect("("); left = ReadSubquery(kind, left, op);
                }
                else { left = new BinaryExpr(op, left, Expression(precedence + 1)); if (negated) left = new UnaryExpr("NOT", left); }
            }
            return left;
        }
        finally { _depth--; }
    }
}
