using CodexChecker;

var tests = new (string Name, Action Body)[]
{
    ("Remaining percent for usedPercent 0", () =>
    {
        AssertEqual(100, RateLimitFormatter.RemainingPercent(new RateLimitWindow(0)));
    }),
    ("Remaining percent for usedPercent 50", () =>
    {
        AssertEqual(50, RateLimitFormatter.RemainingPercent(new RateLimitWindow(50)));
    }),
    ("Remaining percent for usedPercent 100", () =>
    {
        AssertEqual(0, RateLimitFormatter.RemainingPercent(new RateLimitWindow(100)));
    }),
    ("Clamp usedPercent below range", () =>
    {
        AssertEqual(100, RateLimitFormatter.RemainingPercent(new RateLimitWindow(-20)));
    }),
    ("Clamp usedPercent above range", () =>
    {
        AssertEqual(0, RateLimitFormatter.RemainingPercent(new RateLimitWindow(150)));
    }),
    ("Format detail without reset", () =>
    {
        AssertEqual("残り80%", RateLimitFormatter.FormatDetail("5h", new RateLimitWindow(20)));
    }),
    ("Format detail for missing window", () =>
    {
        AssertEqual("データなし", RateLimitFormatter.FormatDetail("5h", null));
    }),
    ("Format 5h reset with hours and minutes", () =>
    {
        var now = new DateTimeOffset(2026, 7, 6, 20, 0, 0, TimeSpan.FromHours(9));
        var resetsAt = now.AddHours(4).AddMinutes(37);
        AssertEqual("残り80%　あと4時間37分でリセット", RateLimitFormatter.FormatDetail("5h", new RateLimitWindow(20, resetsAt), now));
    }),
    ("Format 5h reset spanning days", () =>
    {
        var now = new DateTimeOffset(2026, 7, 6, 20, 0, 0, TimeSpan.FromHours(9));
        var resetsAt = now.AddDays(6).AddHours(23).AddMinutes(30);
        AssertEqual("残り50%　あと6日23時間30分でリセット", RateLimitFormatter.FormatDetail("5h", new RateLimitWindow(50, resetsAt), now));
    }),
    ("Format 5h reset minutes only rounds up", () =>
    {
        var now = new DateTimeOffset(2026, 7, 6, 20, 0, 0, TimeSpan.FromHours(9));
        var resetsAt = now.AddMinutes(25).AddSeconds(30);
        AssertEqual("残り80%　あと26分でリセット", RateLimitFormatter.FormatDetail("5h", new RateLimitWindow(20, resetsAt), now));
    }),
    ("Format 5h reset already elapsed", () =>
    {
        var now = new DateTimeOffset(2026, 7, 6, 20, 0, 0, TimeSpan.FromHours(9));
        var resetsAt = now.AddMinutes(-1);
        AssertEqual("残り80%　まもなくリセット", RateLimitFormatter.FormatDetail("5h", new RateLimitWindow(20, resetsAt), now));
    }),
    ("Format 5h reset from resetsInSeconds", () =>
    {
        var now = new DateTimeOffset(2026, 7, 6, 20, 0, 0, TimeSpan.FromHours(9));
        AssertEqual("残り80%　あと1時間0分でリセット", RateLimitFormatter.FormatDetail("5h", new RateLimitWindow(20, ResetsInSeconds: 3600), now));
    }),
    ("Format 1w reset via ResetsAt", () =>
    {
        var now = new DateTimeOffset(2026, 7, 6, 20, 0, 0, TimeSpan.FromHours(9));
        var resetsAt = now.AddDays(2).AddHours(3);
        AssertEqual("残り50%　あと2日3時間0分でリセット", RateLimitFormatter.FormatDetail("1w", new RateLimitWindow(50, resetsAt), now));
    }),
    ("Format 1w reset from resetsInSeconds", () =>
    {
        var localTime = new DateTime(2026, 7, 6, 20, 0, 0);
        var now = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        AssertEqual("残り50%　あと1日0時間0分でリセット", RateLimitFormatter.FormatDetail("1w", new RateLimitWindow(50, ResetsInSeconds: 86400), now));
    }),
    ("Parse matching rate limit response", () =>
    {
        var line = "{\"id\":3,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":20},\"secondary\":{\"usedPercent\":48,\"resetsInSeconds\":60}}}}";

        AssertTrue(AppServerClient.TryExtractRateLimitsResponse(line, 3, out var snapshot), "response should parse");
        AssertEqual(20, snapshot.Primary?.UsedPercent);
        AssertEqual(48, snapshot.Secondary?.UsedPercent);
        AssertEqual(60, snapshot.Secondary?.ResetsInSeconds);
    }),
    ("Parse decimal usedPercent", () =>
    {
        var line = "{\"id\":3,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":20.5},\"secondary\":{\"usedPercent\":\"48.0\"}}}}";

        AssertTrue(AppServerClient.TryExtractRateLimitsResponse(line, 3, out var snapshot), "response should parse");
        AssertEqual(21, snapshot.Primary?.UsedPercent);
        AssertEqual(48, snapshot.Secondary?.UsedPercent);
    }),
    ("Parse numeric epoch resetsAt", () =>
    {
        var line = "{\"id\":3,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":20,\"resetsAt\":1783348346},\"secondary\":{\"usedPercent\":48,\"resetsAt\":\"1783348346\"}}}}";

        AssertTrue(AppServerClient.TryExtractRateLimitsResponse(line, 3, out var snapshot), "response should parse");
        AssertEqual(DateTimeOffset.FromUnixTimeSeconds(1783348346), snapshot.Primary?.ResetsAt);
        AssertEqual(DateTimeOffset.FromUnixTimeSeconds(1783348346), snapshot.Secondary?.ResetsAt);
    }),
    ("Ignore response with different id", () =>
    {
        var line = "{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":20}}}}";

        AssertTrue(!AppServerClient.TryExtractRateLimitsResponse(line, 3, out _), "different id should be ignored");
    }),
    ("Ignore invalid JSON", () =>
    {
        AssertTrue(!AppServerClient.TryExtractRateLimitsResponse("not json", 3, out _), "invalid JSON should be ignored");
    })
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine($"     {ex.Message}");
    }
}

if (failures > 0)
{
    Environment.ExitCode = 1;
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
