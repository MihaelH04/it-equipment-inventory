namespace ITEquipmentInventory.Services.Search;

public interface ISearchFuzzyMatcher
{
    bool IsMatch(SearchQuery query, params string?[] fields);
    int Distance(string left, string right, int maximumDistance = 2);
}

public sealed class SearchFuzzyMatcher : ISearchFuzzyMatcher
{
    private readonly ISearchNormalizer _normalizer;

    public SearchFuzzyMatcher(ISearchNormalizer normalizer) => _normalizer = normalizer;

    public bool IsMatch(SearchQuery query, params string?[] fields)
    {
        if (query.IsEmpty || query.Normalized.Length < 4)
            return false;

        var candidateTokens = fields
            .SelectMany(_normalizer.Tokenize)
            .Where(x => x.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return query.Groups.All(group => group.Alternatives.Any(alternative =>
        {
            var searchTokens = _normalizer.Tokenize(alternative);
            return searchTokens.All(searchToken => candidateTokens.Any(candidate =>
                candidate.Contains(searchToken, StringComparison.Ordinal) ||
                searchToken.Contains(candidate, StringComparison.Ordinal) ||
                (searchToken.Length >= 4 && Math.Abs(searchToken.Length - candidate.Length) <= 1 &&
                 Distance(searchToken, candidate, 1) <= 1)));
        }));
    }

    public int Distance(string left, string right, int maximumDistance = 2)
    {
        if (left == right) return 0;
        if (Math.Abs(left.Length - right.Length) > maximumDistance) return maximumDistance + 1;

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }
            if (rowMinimum > maximumDistance) return maximumDistance + 1;
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
