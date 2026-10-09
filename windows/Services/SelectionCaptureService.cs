namespace Lexi;

internal interface ISelectionClipboard : IDisposable
{
    nint Foreground { get; }
    bool ModifiersReleased { get; }
    uint Sequence { get; }
    bool TrySnapshot();
    bool SendCopy();
    (string? Text, uint Sequence) ReadText(nint source);
    bool Restore(uint expectedSequence, nint source = 0);
}

public sealed class SelectionCaptureService
{
    private readonly ISelectionClipboard _clipboard;
    internal SelectionCaptureService(ISelectionClipboard clipboard) => _clipboard = clipboard;
    public static string? NormalizeWord(string? text)
    {
        var word = text?.Trim();
        if (string.IsNullOrEmpty(word) || word.Length > 64) return null;
        return System.Text.RegularExpressions.Regex.IsMatch(word, @"^[A-Za-z]+(?:[-'’][A-Za-z]+)*$") ? word : null;
    }

    // All clipboard work runs on one worker thread. Never block Avalonia's dispatcher.
    public Task<string?> CaptureAsync(nint source) => Task.Run(() => Capture(source));

    private string? Capture(nint source)
    {
        using var clipboard = _clipboard;
        if (source == 0 || clipboard.Foreground != source) return null;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!clipboard.ModifiersReleased && timer.ElapsedMilliseconds < 450) Thread.Sleep(15);
        if (!clipboard.ModifiersReleased || clipboard.Foreground != source) return null;
        var before = clipboard.Sequence;
        if (!clipboard.TrySnapshot() || clipboard.Sequence != before || clipboard.Foreground != source || !clipboard.SendCopy()) return null;
        timer.Restart();
        while (clipboard.Sequence == before && timer.ElapsedMilliseconds < 400) Thread.Sleep(15);
        if (clipboard.Sequence == before || clipboard.Foreground != source) return null; // Ctrl+C did nothing: never use stale text.
        var observed = clipboard.Sequence;
        var copied = clipboard.ReadText(source);
        if (copied.Sequence == 0 || copied.Sequence != observed || copied.Sequence == before || clipboard.Foreground != source) return null;
        // Restore only our copy; a later user clipboard update always wins.
        if (clipboard.Sequence == copied.Sequence) clipboard.Restore(copied.Sequence, source);
        return clipboard.Foreground == source ? NormalizeWord(copied.Text) : null;
    }
}
