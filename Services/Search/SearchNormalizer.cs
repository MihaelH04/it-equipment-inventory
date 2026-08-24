using System.Globalization;
using System.Text;

namespace ITEquipmentInventory.Services.Search;

public interface ISearchNormalizer
{
    string Normalize(string? value);
    string CompactCode(string? value);
    IReadOnlyList<string> Tokenize(string? value);
}

public sealed class SearchNormalizer : ISearchNormalizer
{
    public const int MaximumQueryLength = 200;

    public string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var input = value.Length > MaximumQueryLength
            ? value[..MaximumQueryLength]
            : value;
        var decomposed = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            var mapped = character switch
            {
                'č' or 'ć' => 'c',
                'š' => 's',
                'ž' => 'z',
                'đ' => 'd',
                _ => character
            };

            if (char.IsLetterOrDigit(mapped))
            {
                if (pendingSpace && result.Length > 0)
                    result.Append(' ');
                result.Append(mapped);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return result.ToString();
    }

    public string CompactCode(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
            return string.Empty;

        return string.Concat(normalized.Where(char.IsLetterOrDigit));
    }

    public IReadOnlyList<string> Tokenize(string? value) => Normalize(value)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
