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
