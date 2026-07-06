namespace CodexChecker;

public sealed record RateLimitSnapshot(RateLimitWindow? Primary, RateLimitWindow? Secondary);

public sealed record RateLimitWindow(int UsedPercent, DateTimeOffset? ResetsAt = null, int? ResetsInSeconds = null);

public static class RateLimitFormatter
{
    public static int RemainingPercent(RateLimitWindow window)
        => Math.Clamp(100 - window.UsedPercent, 0, 100);

    public static string FormatDetail(string label, RateLimitWindow? window)
    {
        if (window is null)
        {
            return "データなし";
        }

        return $"残り{RemainingPercent(window)}%{FormatReset(label, window)}";
    }

    private static string FormatReset(string label, RateLimitWindow window)
    {
        var resetsAt = window.ResetsAt;
        if (resetsAt is null && window.ResetsInSeconds is int seconds)
        {
            resetsAt = DateTimeOffset.Now.AddSeconds(seconds);
        }

        if (resetsAt is null)
        {
            return string.Empty;
        }

        var format = label == "1w" ? "yyyy-MM-dd HH:mm" : "HH:mm";
        return $"　{resetsAt.Value.LocalDateTime.ToString(format)}";
    }
}
