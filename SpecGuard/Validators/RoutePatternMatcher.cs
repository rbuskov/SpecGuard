using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace SpecGuard.Validators;

internal sealed class RoutePatternMatcher
{
    private readonly TemplateMatcher matcher;

    public RoutePatternMatcher(string pattern)
    {
        Pattern = pattern;

        var template = TemplateParser.Parse(pattern.TrimStart('/'));
        LiteralSegmentCount = template.Segments.Count(s =>
            s.Parts.Count == 1 && !s.Parts[0].IsParameter);
        matcher = new TemplateMatcher(template, defaults: new RouteValueDictionary());
    }

    public string Pattern { get; }

    public int LiteralSegmentCount { get; }

    public bool IsMatch(string path) => matcher.TryMatch(new PathString("/" + path.TrimStart('/')), new RouteValueDictionary());
}
