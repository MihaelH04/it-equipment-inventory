namespace ITEquipmentInventory.Models.ViewModels;

public static class PaginationConstants
{
    public const int DefaultPageSize = 25;
    public static readonly int[] AllowedPageSizes = [25, 50, 100];
    public const int MaxSearchSuggestionResults = 2000;
    public const int MaxAutocompleteResults = 300;
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public int PageSize { get; init; } = PaginationConstants.DefaultPageSize;
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
}
