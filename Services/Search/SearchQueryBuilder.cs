using System.Linq.Expressions;

namespace ITEquipmentInventory.Services.Search;

public sealed record SearchField<TEntity>(Expression<Func<TEntity, string?>> Selector, int Weight = 10, bool IsCode = false);

public interface ISearchQueryBuilder
{
    IQueryable<TEntity> WhereMatches<TEntity>(IQueryable<TEntity> source, SearchQuery query, params SearchField<TEntity>[] fields);
    IOrderedQueryable<TEntity> OrderByRelevance<TEntity>(IQueryable<TEntity> source, SearchQuery query, params SearchField<TEntity>[] fields);
}

public sealed class SearchQueryBuilder : ISearchQueryBuilder
{
    private static readonly string[] Separators = ["_", "-", "/", ",", ".", "(", ")", ":", ";", "\\", "\t", "\r", "\n"];

    public IQueryable<TEntity> WhereMatches<TEntity>(IQueryable<TEntity> source, SearchQuery query, params SearchField<TEntity>[] fields)
    {
        if (query.IsEmpty || fields.Length == 0)
            return source;

        var parameter = Expression.Parameter(typeof(TEntity), "entity");

        // Unos poput RAD-001, INV-10001 ili LPT-20260001 predstavlja jednu šifru.
        // Dijelovi takve šifre ne smiju se pronaći u različitim poljima zapisa.
        if (LooksLikeCode(query.Original) && query.Compact.Length > 0)
        {
            Expression? anyCodeField = null;
            foreach (var field in fields)
            {
                var value = ReplaceParameter(field.Selector, parameter);
                var compactField = CompactSql(NormalizeSql(value));
                var match = Expression.Call(
                    compactField,
                    nameof(string.Contains),
                    Type.EmptyTypes,
                    Expression.Constant(query.Compact));
                anyCodeField = anyCodeField == null ? match : Expression.OrElse(anyCodeField, match);
            }

            if (anyCodeField != null)
                return source.Where(Expression.Lambda<Func<TEntity, bool>>(anyCodeField, parameter));
        }

        Expression? allGroups = null;

        foreach (var group in query.Groups)
        {
            Expression? anyField = null;
            foreach (var field in fields)
            {
                var value = ReplaceParameter(field.Selector, parameter);
                var normalizedField = NormalizeSql(value);
                var compactField = CompactSql(normalizedField);

                foreach (var alternative in group.Alternatives)
                {
                    var normalizedAlternative = NormalizeLiteral(alternative);
                    if (normalizedAlternative.Length == 0)
                        continue;

                    var contains = Expression.Call(normalizedField, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(normalizedAlternative));
                    var compactAlternative = CompactLiteral(normalizedAlternative);
                    Expression match = contains;
                    if (compactAlternative.Length > 0)
                    {
                        var compactContains = Expression.Call(compactField, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(compactAlternative));
                        match = Expression.OrElse(match, compactContains);
                    }
                    anyField = anyField == null ? match : Expression.OrElse(anyField, match);
                }
            }

            if (anyField != null)
                allGroups = allGroups == null ? anyField : Expression.AndAlso(allGroups, anyField);
        }

        if (allGroups == null)
            return source;

        return source.Where(Expression.Lambda<Func<TEntity, bool>>(allGroups, parameter));
    }

    public IOrderedQueryable<TEntity> OrderByRelevance<TEntity>(IQueryable<TEntity> source, SearchQuery query, params SearchField<TEntity>[] fields)
    {
        if (query.IsEmpty || fields.Length == 0)
            return source.OrderBy(_ => 0);

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression score = Expression.Constant(0);

        foreach (var field in fields)
        {
            var value = ReplaceParameter(field.Selector, parameter);
            var normalizedField = NormalizeSql(value);
            var compactField = CompactSql(normalizedField);
            var exact = Expression.Equal(normalizedField, Expression.Constant(query.Normalized));
            var starts = Expression.Call(normalizedField, nameof(string.StartsWith), Type.EmptyTypes, Expression.Constant(query.Normalized));
            Expression fieldScore = Expression.Condition(exact, Expression.Constant(1000 + field.Weight * 10),
                Expression.Condition(starts, Expression.Constant(600 + field.Weight * 5), Expression.Constant(0)));

            if (field.IsCode && query.Compact.Length > 0)
            {
                var compactExact = Expression.Equal(compactField, Expression.Constant(query.Compact));
                fieldScore = Expression.Add(fieldScore,
                    Expression.Condition(compactExact, Expression.Constant(950 + field.Weight * 10), Expression.Constant(0)));
            }

            foreach (var group in query.Groups)
            {
                Expression? tokenMatch = null;
                foreach (var alternative in group.Alternatives)
                {
                    var literal = NormalizeLiteral(alternative);
                    if (literal.Length == 0) continue;
                    var contains = Expression.Call(normalizedField, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(literal));
                    tokenMatch = tokenMatch == null ? contains : Expression.OrElse(tokenMatch, contains);
                }
                if (tokenMatch != null)
                    fieldScore = Expression.Add(fieldScore, Expression.Condition(tokenMatch, Expression.Constant(field.Weight), Expression.Constant(0)));
            }

            score = Expression.Add(score, fieldScore);
        }

        return source.OrderByDescending(Expression.Lambda<Func<TEntity, int>>(score, parameter));
    }

    private static Expression ReplaceParameter<TEntity>(Expression<Func<TEntity, string?>> selector, ParameterExpression parameter) =>
        new ParameterReplaceVisitor(selector.Parameters[0], parameter).Visit(selector.Body)!;

    private static Expression NormalizeSql(Expression value)
    {
        Expression result = Expression.Coalesce(value, Expression.Constant(string.Empty));
        result = Expression.Call(result, nameof(string.ToLower), Type.EmptyTypes);
        foreach (var (from, to) in new[]
        {
            ("č", "c"), ("Č", "c"), ("ć", "c"), ("Ć", "c"), ("š", "s"), ("Š", "s"),
            ("ž", "z"), ("Ž", "z"), ("đ", "d"), ("Đ", "d")
        })
            result = Expression.Call(result, nameof(string.Replace), Type.EmptyTypes, Expression.Constant(from), Expression.Constant(to));
        foreach (var separator in Separators)
            result = Expression.Call(result, nameof(string.Replace), Type.EmptyTypes, Expression.Constant(separator), Expression.Constant(" "));
        return result;
    }

    private static Expression CompactSql(Expression normalized)
    {
        var result = normalized;
        result = Expression.Call(result, nameof(string.Replace), Type.EmptyTypes, Expression.Constant(" "), Expression.Constant(string.Empty));
        return result;
    }

    private static string NormalizeLiteral(string value)
    {
        var result = value.ToLowerInvariant()
            .Replace("č", "c").Replace("ć", "c").Replace("š", "s").Replace("ž", "z").Replace("đ", "d");
        foreach (var separator in Separators)
            result = result.Replace(separator, " ");
        return string.Join(' ', result.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string CompactLiteral(string value) => string.Concat(value.Where(char.IsLetterOrDigit));

    private static bool LooksLikeCode(string value) =>
        !value.Any(char.IsWhiteSpace) && value.Any(char.IsLetter) && value.Any(char.IsDigit);

    private sealed class ParameterReplaceVisitor(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }
}
