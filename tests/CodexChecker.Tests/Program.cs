using CodexChecker;

var tests = new (string Name, Action Body)[]
{
    ("Format usedPercent 0 as full remaining", () =>
    {
        AssertEqual("5h [##########] 残り100%", RateLimitFormatter.FormatWindow("5h", new RateLimitWindow(0)));
    }),
    ("Format usedPercent 50 as half remaining", () =>
    {
        AssertEqual("1w [#####-----] 残り50%", RateLimitFormatter.FormatWindow("1w", new RateLimitWindow(50)));
    }),
    ("Format usedPercent 100 as empty remaining", () =>
    {
        AssertEqual("5h [----------] 残り0%", RateLimitFormatter.FormatWindow("5h", new RateLimitWindow(100)));
    }),
    ("Clamp usedPercent below range", () =>
    {
        AssertEqual("5h [##########] 残り100%", RateLimitFormatter.FormatWindow("5h", new RateLimitWindow(-20)));
    }),
    ("Clamp usedPercent above range", () =>
    {
        AssertEqual("1w [----------] 残り0%", RateLimitFormatter.FormatWindow("1w", new RateLimitWindow(150)));
    }),
    ("Parse matching rate limit response", () =>
    {
        var line = "{\"id\":3,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":20},\"secondary\":{\"usedPercent\":48,\"resetsInSeconds\":60}}}}";

        AssertTrue(AppServerClient.TryExtractRateLimitsResponse(line, 3, out var snapshot), "response should parse");
        AssertEqual(20, snapshot.Primary?.UsedPercent);
        AssertEqual(48, snapshot.Secondary?.UsedPercent);
        AssertEqual(60, snapshot.Secondary?.ResetsInSeconds);
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
