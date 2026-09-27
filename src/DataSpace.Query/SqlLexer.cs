using System.Text;
using DataSpace.Core;

namespace DataSpace.Query;

internal enum TokenKind { Word, QuotedName, String, Number, Date, Parameter, Symbol, End }
internal readonly record struct Token(TokenKind Kind, string Text, int Position);

internal static class SqlLexer
{
    public static List<Token> Read(string sql)
    {
        if (sql.Length > 65536) throw new DataSpaceException("SQL is limited to 65,536 characters.");
        var tokens = new List<Token>(); var i = 0;
        while (i < sql.Length)
        {
            if (tokens.Count > 20000) throw new DataSpaceException("The SQL statement contains too many tokens.");
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                // Native multiline editors can use CR alone. Never swallow a later
                // WHERE clause merely because its line ending is not LF.
                while (i < sql.Length && sql[i] is not '\n' and not '\r') i++;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw Error("Unterminated comment", i);
                i = end + 2; continue;
            }
            var start = i++;
            if (c is '\'' or '"' or '[' or '#')
            {
                var close = c == '[' ? ']' : c; var text = new StringBuilder(); var closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == close)
                    {
                        i++;
                        if (c != '#' && i < sql.Length && sql[i] == close) { text.Append(close); i++; continue; }
                        closed = true; break;
                    }
                    text.Append(sql[i++]);
                }
                if (!closed) throw Error("Unterminated quoted value", start);
                tokens.Add(new(c == '[' ? TokenKind.QuotedName : c == '#' ? TokenKind.Date : TokenKind.String, text.ToString(), start));
            }
            else if (char.IsDigit(c) || c == '.' && i < sql.Length && char.IsDigit(sql[i]))
            {
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                if (i < sql.Length && sql[i] is 'e' or 'E')
                { i++; if (i < sql.Length && sql[i] is '+' or '-') i++; while (i < sql.Length && char.IsDigit(sql[i])) i++; }
                tokens.Add(new(TokenKind.Number, sql[start..i], start));
            }
            else if (char.IsLetter(c) || c is '_' or '@')
            {
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                tokens.Add(new(c == '@' ? TokenKind.Parameter : TokenKind.Word, sql[(c == '@' ? start + 1 : start)..i], start));
            }
            else
            {
                if (i < sql.Length && (c is '<' or '>' or '!' && sql[i] == '=' || c == '<' && sql[i] == '>')) i++;
                var text = sql[start..i];
                if (!new[] { ",", ".", "(", ")", ";", "*", "+", "-", "/", "%", "&", "=", "<", ">", "<=", ">=", "<>", "!=" }.Contains(text))
                    throw Error($"Unexpected character '{c}'", start);
                tokens.Add(new(TokenKind.Symbol, text, start));
            }
        }
        tokens.Add(new(TokenKind.End, "", sql.Length)); return tokens;
    }
    internal static DataSpaceException Error(string message, int position) => new($"{message} at character {position + 1}.");
}
