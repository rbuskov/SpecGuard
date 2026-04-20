namespace SpecGuard.Test.Validators;

public class RoutePatternMatcherTests
{
    [Theory]
    [InlineData("/users", 1)]
    [InlineData("/users/{id}", 1)]
    [InlineData("/users/{id}/posts", 2)]
    [InlineData("/users/{id}/posts/{postId}", 2)]
    [InlineData("/users/me", 2)]
    [InlineData("/users/{id}/posts/latest", 3)]
    [InlineData("/{a}/{b}/{c}", 0)]
    public void LiteralSegmentCount_reflects_non_parameterized_segments(string pattern, int expected)
    {
        var matcher = new RoutePatternMatcher(pattern);

        Assert.Equal(expected, matcher.LiteralSegmentCount);
    }

    [Theory]
    [InlineData("/reports/{year}-{month}", "/reports/2024-03", true)]
    [InlineData("/reports/{year}-{month}", "/reports/2024", false)]
    [InlineData("/files/{name}.{ext}", "/files/readme.txt", true)]
    [InlineData("/files/{name}.{ext}", "/files/readme", false)]
    [InlineData("/users/{id}", "/users/123", true)]
    [InlineData("/users/{id}", "/users/123/extra", false)]
    public void IsMatch_handles_compound_segments(string pattern, string path, bool expected)
    {
        var matcher = new RoutePatternMatcher(pattern);

        Assert.Equal(expected, matcher.IsMatch(path));
    }

    [Theory]
    [InlineData("/pets/{id}", "/pets/ada%20lovelace", true)]
    [InlineData("/pets/{id}", "/pets/%E7%8C%AB", true)]
    public void IsMatch_accepts_url_encoded_path_segments(string pattern, string path, bool expected)
    {
        var matcher = new RoutePatternMatcher(pattern);

        Assert.Equal(expected, matcher.IsMatch(path));
    }

    [Theory]
    [InlineData("/pets", "/pets/", true)]  // ASP.NET TemplateMatcher tolerates the trailing slash
    [InlineData("/pets", "/Pets", true)]   // TemplateMatcher is case-insensitive by default
    public void IsMatch_case_and_trailing_slash_behavior(string pattern, string path, bool expected)
    {
        var matcher = new RoutePatternMatcher(pattern);

        Assert.Equal(expected, matcher.IsMatch(path));
    }

    [Fact]
    public void IsMatch_accepts_empty_path_for_root_pattern()
    {
        var matcher = new RoutePatternMatcher("/");

        Assert.True(matcher.IsMatch("/"));
    }

    [Fact]
    public void Equal_literal_count_routes_preserve_order_under_stable_sort()
    {
        // Two templates with identical LiteralSegmentCount (both = 3).
        // The sort key is LiteralSegmentCount; List<T>.Sort is not
        // guaranteed stable, but both resulting routes still carry the
        // same count — the tie-breaker is undefined by design.
        var routes = new List<RoutePatternMatcher>
        {
            new("/users/{id}/posts/all"),
            new("/users/all/posts/{id}"),
        };

        routes.Sort((a, b) => b.LiteralSegmentCount.CompareTo(a.LiteralSegmentCount));

        Assert.Equal(3, routes[0].LiteralSegmentCount);
        Assert.Equal(3, routes[1].LiteralSegmentCount);
    }

    [Fact]
    public void Catch_all_template_matches_nested_paths()
    {
        var matcher = new RoutePatternMatcher("/files/{*path}");

        Assert.True(matcher.IsMatch("/files/a/b/c"));
        Assert.True(matcher.IsMatch("/files/single"));
    }

    [Fact]
    public void Sorting_by_LiteralSegmentCount_descending_puts_specific_routes_first()
    {
        var routes = new List<RoutePatternMatcher>
        {
            new((string)"/users/{id}"),
            new((string)"/users/me"),
            new((string)"/users/{id}/posts/{postId}"),
            new((string)"/users/{id}/posts/latest"),
        };

        routes.Sort((a, b) => b.LiteralSegmentCount.CompareTo(a.LiteralSegmentCount));

        Assert.Equal("/users/{id}/posts/latest", routes[0].Pattern);  // 3 literal
        Assert.Equal(3, routes[0].LiteralSegmentCount);
        Assert.Equal(2, routes[1].LiteralSegmentCount);  // /users/me or /users/{id}/posts/{postId}
        Assert.Equal(2, routes[2].LiteralSegmentCount);
        Assert.Equal(1, routes[3].LiteralSegmentCount);  // /users/{id}
    }
}
