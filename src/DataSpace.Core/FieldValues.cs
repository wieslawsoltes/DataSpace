using System.Globalization;

namespace DataSpace.Core;

public static class FieldValues
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    public static object? Parse(FieldDefinition field, string? value)
    {
        if (value is null || (value.Length == 0 && field.Type is not (FieldType.ShortText or FieldType.LongText))) return null;
        try
        {
            return field.Type switch
            {
                FieldType.ShortText or FieldType.LongText => value,
                FieldType.AutoNumber or FieldType.Integer => long.Parse(value, NumberStyles.Integer, Culture),
                FieldType.Decimal or FieldType.Currency => decimal.Parse(value, NumberStyles.Number, Culture),
                FieldType.DateTime => DateTime.Parse(value, Culture, DateTimeStyles.RoundtripKind),
                FieldType.YesNo => value.Trim().ToUpperInvariant() switch
                {
                    "TRUE" or "YES" or "1" or "-1" => true,
                    "FALSE" or "NO" or "0" => false,
                    _ => throw new FormatException()
                },
                FieldType.Guid => System.Guid.Parse(value),
                _ => throw new DataSpaceException("Unsupported field type.")
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new DataSpaceException($"'{value}' is not a valid {field.Type} value for '{field.Name}'.");
        }
    }
    public static string? Normalize(FieldDefinition field, string? value)
    {
        var typed = Parse(field, value);
        if (typed is null)
        {
            if (field.Required || field.PrimaryKey) throw new DataSpaceException($"'{field.Name}' is required.");
            return null;
        }
        if (typed is string text)
        {
            if (!field.AllowZeroLength && text.Length == 0) throw new DataSpaceException($"'{field.Name}' cannot be empty.");
            if (field.Type == FieldType.ShortText && text.Length > field.MaxLength)
                throw new DataSpaceException($"'{field.Name}' allows at most {field.MaxLength} characters.");
            return text;
        }
        if (typed is DateTime date) return date.ToString("O", Culture);
        if (typed is decimal number)
        {
            if (field.Type == FieldType.Currency) number = decimal.Round(number, 4);
            // Equal decimal values must have equal persistent keys regardless of scale.
            // Fixed notation also round-trips tiny values through NumberStyles.Number.
            return number.ToString("0.############################", Culture);
        }
        return Convert.ToString(typed, Culture);
    }
    public static string Display(FieldDefinition field, string? value)
    {
        var typed = Parse(field, value);
        if (typed is null) return "";
        try
        {
            if (typed is DateTime date) return date.ToString(string.IsNullOrEmpty(field.Format) ? "dd MMM yyyy" : field.Format, Culture);
            if (typed is decimal number) return number.ToString(string.IsNullOrEmpty(field.Format)
                ? (field.Type == FieldType.Currency ? "#,##0.00" : "0.############################") : field.Format, Culture);
            if (typed is bool yes) return yes ? "Yes" : "No";
            return Convert.ToString(typed, Culture) ?? "";
        }
        catch (FormatException) { return value ?? ""; }
    }
    public static string? FromObject(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("O", Culture),
        _ => Convert.ToString(value, Culture)
    };
    public static string Key(IEnumerable<string?> values) => string.Concat(values.Select(v => v is null ? "-1:" : v.Length + ":" + v));
}
