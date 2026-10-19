namespace Lexi;

/// <summary>
/// Tracks which book/section scopes have already received their one-time
/// default selection during a plan-editing session.
/// </summary>
public sealed class PlanScopeInitializationTracker
{
    private readonly HashSet<string> _initializedKeys = new(StringComparer.Ordinal);

    public bool ShouldSelectEntireScope(string bookId, string? sectionId, bool wholeBook)
        => _initializedKeys.Add(BuildKey(bookId, sectionId, wholeBook));

    public void MarkInitialized(string bookId, string? sectionId, bool wholeBook)
        => _initializedKeys.Add(BuildKey(bookId, sectionId, wholeBook));

    public bool IsInitialized(string bookId, string? sectionId, bool wholeBook)
        => _initializedKeys.Contains(BuildKey(bookId, sectionId, wholeBook));

    private static string BuildKey(string bookId, string? sectionId, bool wholeBook)
    {
        if (string.IsNullOrWhiteSpace(bookId))
            throw new ArgumentException("Book ID is required.", nameof(bookId));

        var scope = wholeBook ? "__whole_book__" : sectionId?.Trim();
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("A section ID is required unless the whole book is selected.", nameof(sectionId));

        return bookId.Trim() + "\u001f" + scope;
    }
}
