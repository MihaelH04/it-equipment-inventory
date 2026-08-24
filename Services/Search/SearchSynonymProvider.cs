namespace ITEquipmentInventory.Services.Search;

public interface ISearchSynonymProvider
{
    IReadOnlyCollection<string> Expand(string token);
}

public sealed class SearchSynonymProvider : ISearchSynonymProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _lookup;

    public SearchSynonymProvider(ISearchNormalizer normalizer)
    {
        string[][] groups =
        [
            ["printer", "pisač", "pisac"],
            ["laptop", "notebook", "prijenosno", "prijenosno računalo"],
            ["pc", "računalo", "racunalo", "desktop", "stolno računalo"],
            ["monitor", "display", "ekran"],
            ["multifunkcijski uređaj", "multifunkcijski uredaj", "mfp"],
            ["radna stanica", "workstation"],
            ["inventurni broj", "inventar", "inventurni"],
            ["serijski broj", "serial", "serijski", "sn", "s/n"],
            ["originalni", "original"],
            ["zamjenski", "kompatibilni", "zamjena"],
            ["aktivan", "aktivno", "active"],
            ["neaktivan", "neaktivno", "inactive"],
            ["zaduženo", "zaduzeno", "dodijeljeno"],
            ["dostupno", "slobodno"],
            ["naručeno", "naruceno", "dolazi"]
        ];

        var groupedLookup = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var normalizedValues = group
                .Select(normalizer.Normalize)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var value in normalizedValues)
            {
                AddAlternatives(value, normalizedValues);
                foreach (var token in normalizer.Tokenize(value))
                    AddAlternatives(token, normalizedValues);
            }
        }

        _lookup = groupedLookup.ToDictionary(
            x => x.Key,
            x => (IReadOnlyCollection<string>)x.Value.ToArray(),
            StringComparer.Ordinal);

        void AddAlternatives(string key, IEnumerable<string> alternatives)
        {
            if (!groupedLookup.TryGetValue(key, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                groupedLookup[key] = values;
            }
            values.UnionWith(alternatives);
        }
    }

    public IReadOnlyCollection<string> Expand(string token) =>
        _lookup.TryGetValue(token, out var values) ? values : [token];
}
