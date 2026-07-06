namespace CodexChecker;

public sealed record RateLimitSnapshot(RateLimitWindow? Primary, RateLimitWindow? Secondary);

public sealed record RateLimitWindow(int UsedPercent, DateTimeOffset? ResetsAt = null, int? ResetsInSeconds = null);

public static class RateLimitFormatter
{
    public static int RemainingPercent(RateLimitWindow window)
        => Math.Clamp(100 - window.UsedPercent, 0, 100);

    public static string FormatDetail(string label, RateLimitWindow? window, DateTimeOffset? now = null)
    {
        if (window is null)
        {
            return "データなし";
        }

        return $"残り{RemainingPercent(window)}%{FormatReset(label, window, now ?? DateTimeOffset.Now)}";
    }

    private static string FormatReset(string label, RateLimitWindow window, DateTimeOffset now)
    {
        var resetsAt = window.ResetsAt;
        if (resetsAt is null && window.ResetsInSeconds is int seconds)
        {
            resetsAt = now.AddSeconds(seconds);
        }

        if (resetsAt is null)
        {
            return string.Empty;
        }

        return $"　{FormatRemaining(resetsAt.Value - now)}";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "まもなくリセット";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"あと{(int)remaining.TotalDays}日{remaining.Hours}時間{remaining.Minutes}分でリセット";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"あと{(int)remaining.TotalHours}時間{remaining.Minutes}分でリセット";
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"あと{minutes}分でリセット";
    }
}
