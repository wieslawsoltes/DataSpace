using System.Globalization;
using System.Text;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private sealed record CorrelationSlot(int Depth, string Name);

    private static CorrelationSlot[]? BindCorrelation(IEnumerable<OuterReference> references, EvaluationContext schema)
    {
        var slots = new List<CorrelationSlot>();
        foreach (var reference in references)
        {
            var depth = 0;
            EvaluationContext? owner = schema;
            while (owner is not null && !ReferenceEquals(owner, reference.Owner)) { owner = owner.Outer; depth++; }
            // Conservative fallback for references outside the lexical ancestry.
            if (owner is null) return null;
            var slot = new CorrelationSlot(depth, reference.Name.Name);
            if (!slots.Contains(slot)) slots.Add(slot);
            if (slots.Count > 64) return null;
        }
        return slots.ToArray();
    }

    private static string? CorrelationKey(EvaluationContext row, IReadOnlyList<CorrelationSlot> slots, int maximumCharacters)
    {
        if (maximumCharacters < 1) return null;
        var key = new StringBuilder(Math.Min(128, maximumCharacters));
        foreach (var slot in slots)
        {
            EvaluationContext? owner = row;
            for (var depth = 0; depth < slot.Depth && owner is not null; depth++) owner = owner.Outer;
            if (owner is null || !owner.Values.TryGetValue(slot.Name, out var value)) return null;
            // Cache identity must be stricter than SQL equality: CStr/concatenation
            // can observe case and decimal scale. Do not use case-folded join keys.
            if (value is string large && large.Length > maximumCharacters - key.Length - 16) return null;
            var encoded = value switch
            {
                null => "n",
                string text => "s" + text,
                bool flag => flag ? "b1" : "b0",
                int number => "i" + number.ToString(CultureInfo.InvariantCulture),
                long number => "l" + number.ToString(CultureInfo.InvariantCulture),
                decimal number => "d" + number.ToString(CultureInfo.InvariantCulture),
                double number => "f" + BitConverter.DoubleToInt64Bits(number).ToString(CultureInfo.InvariantCulture),
                DateTime date => "t" + date.ToBinary().ToString(CultureInfo.InvariantCulture),
                Guid guid => "g" + guid.ToString("D"),
                _ => null
            };
            if (encoded is null || key.Length + encoded.Length + 16 > maximumCharacters) return null;
            key.Append(encoded.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(encoded);
        }
        return key.ToString();
    }
}
