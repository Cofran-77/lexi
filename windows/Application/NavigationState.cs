namespace Lexi;

/// <summary>
/// 3.3.1 冷启动页面恢复白名单。只恢复稳定的顶层页面；review/focus 走各自学习断点，
/// social/quotes/settings 走抽屉或实时视图，不进入跨会话恢复。
/// </summary>
public static class NavigationState
{
    public const string FallbackPage = "lookup";

    private static readonly string[] Restorable =
        ["lookup", "vocab", "learning", "ielts", "exams", "statistics"];

    public static bool IsRestorable(string? page) =>
        page is not null && Restorable.Contains(page, StringComparer.Ordinal);

    /// <summary>白名单外（含空值、旧值、损坏值）一律回到查词页。</summary>
    public static string Normalize(string? page) => IsRestorable(page) ? page! : FallbackPage;
}
