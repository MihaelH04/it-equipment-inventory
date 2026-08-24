namespace ITEquipmentInventory.Services.Search;

public sealed record SearchTokenGroup(string Token, IReadOnlyCollection<string> Alternatives);

public sealed record SearchQuery(
    string Original,
    string Normalized,
    string Compact,
    IReadOnlyList<SearchTokenGroup> Groups)
{
    public bool IsEmpty => Groups.Count == 0;
}

public interface ISearchQueryService
{
    SearchQuery Parse(string? input);
    bool Matches(SearchQuery query, params string?[] fields);
    int Score(SearchQuery query, params SearchValue[] fields);
}

public sealed record SearchValue(string? Value, int Weight = 10, bool IsCode = false);

public sealed class SearchQueryService : ISearchQueryService
{
    private readonly ISearchNormalizer _normalizer;
    private readonly ISearchSynonymProvider _synonyms;

    public SearchQueryService(ISearchNormalizer normalizer, ISearchSynonymProvider synonyms)
    {
        _normalizer = normalizer;
        _synonyms = synonyms;
    }

    public SearchQuery Parse(string? input)
    {
        var original = (input ?? string.Empty).Trim();
        if (original.Length > SearchNormalizer.MaximumQueryLength)
            original = original[..SearchNormalizer.MaximumQueryLength];

        var normalized = _normalizer.Normalize(original);
        var tokens = _normalizer.Tokenize(normalized).ToList();
        if (tokens.Count > 1 && tokens[0] == "sn")
            tokens.RemoveAt(0);
        else if (tokens.Count > 2 && tokens[0] == "s" && tokens[1] == "n")
            tokens.RemoveRange(0, 2);
        else if (tokens.Count > 2 && tokens[0] == "serijski" && tokens[1] == "broj")
            tokens.RemoveRange(0, 2);
        else if (tokens.Count > 2 && tokens[0] == "inventurni" && tokens[1] == "broj")
            tokens.RemoveRange(0, 2);

        var groups = tokens
            .Select(token => new SearchTokenGroup(token, ExpandToken(token)))
            .ToArray();

        return new SearchQuery(original, normalized, _normalizer.CompactCode(normalized), groups);
    }

    public bool Matches(SearchQuery query, params string?[] fields)
    {
        if (query.IsEmpty)
            return true;

        var normalizedFields = fields
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => (_normalizer.Normalize(x), _normalizer.CompactCode(x)))
            .ToArray();

        return query.Groups.All(group => group.Alternatives.Any(alternative =>
        {
            var normalized = _normalizer.Normalize(alternative);
            var compact = _normalizer.CompactCode(alternative);
            return normalizedFields.Any(field =>
                field.Item1.Contains(normalized, StringComparison.Ordinal) ||
                (compact.Length > 0 && field.Item2.Contains(compact, StringComparison.Ordinal)));
        }));
    }

    public int Score(SearchQuery query, params SearchValue[] fields)
    {
        if (query.IsEmpty)
            return 0;

        var score = 0;
        foreach (var field in fields.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var normalized = _normalizer.Normalize(field.Value);
            var compact = _normalizer.CompactCode(field.Value);

            if (normalized == query.Normalized)
                score += 1000 + field.Weight * 10;
            else if (field.IsCode && compact == query.Compact)
                score += 950 + field.Weight * 10;
            else if (normalized.StartsWith(query.Normalized, StringComparison.Ordinal))
                score += 600 + field.Weight * 5;

            foreach (var group in query.Groups)
            {
                if (group.Alternatives.Any(x => normalized.Contains(_normalizer.Normalize(x), StringComparison.Ordinal)))
                    score += field.Weight;
            }
        }

        return score;
    }

    private IReadOnlyCollection<string> ExpandToken(string token)
    {
        var values = _synonyms.Expand(token).ToHashSet(StringComparer.Ordinal);
        var compact = _normalizer.CompactCode(token);
        if (compact.StartsWith("sn", StringComparison.Ordinal) && compact.Length > 2)
            values.Add(compact[2..]);
        return values;
    }
}
