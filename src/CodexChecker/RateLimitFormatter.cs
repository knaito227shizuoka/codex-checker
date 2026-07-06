namespace CodexChecker;

public sealed record RateLimitSnapshot(RateLimitWindow? Primary, RateLimitWindow? Secondary);

public sealed record RateLimitWindow(int UsedPercent, DateTimeOffset? ResetsAt = null, int? ResetsInSeconds = null);

public static class RateLimitFormatter
{
    public static string FormatWindow(string label, RateLimitWindow? window)
    {
        if (window is null)
        {
            return $"{label} データなし";
        }

        var remaining = Math.Clamp(100 - window.UsedPercent, 0, 100);
        var filled = Math.Clamp((int)Math.Round(remaining / 10.0, MidpointRounding.AwayFromZero), 0, 10);
        var bar = "[" + new string('#', filled) + new string('-', 10 - filled) + "]";
        var resetText = FormatReset(label, window);

        return $"{label} {bar} 残り{remaining}%{resetText}";
    }

    public static string FormatSnapshot(RateLimitSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "利用量を取得できませんでした。";
        }

        return string.Join(Environment.NewLine, new[]
        {
            FormatWindow("5h", snapshot.Primary),
            FormatWindow("1w", snapshot.Secondary)
        });
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
        return $"　R: {resetsAt.Value.LocalDateTime.ToString(format)}";
    }
}
