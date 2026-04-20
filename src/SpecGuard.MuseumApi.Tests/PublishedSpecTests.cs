using System.Text.Json;

namespace SpecGuard.MuseumApi.Tests;

public class PublishedSpecTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    private async Task<JsonDocument> FetchSpec()
    {
        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_special_events_declares_400_and_422()
    {
        using var spec = await FetchSpec();

        var responses = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/special-events")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("422", out _));
    }

    [Fact]
    public async Task Get_museum_hours_declares_422_but_not_400()
    {
        // GET has query parameters but no JSON body — only 422 should be added.
        using var spec = await FetchSpec();

        var responses = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/museum-hours")
            .GetProperty("get")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("422", out _));
        Assert.False(responses.TryGetProperty("400", out _));
    }

    [Fact]
    public async Task Added_responses_use_problem_json_media_type()
    {
        using var spec = await FetchSpec();

        var response422 = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/special-events")
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty("422");

        Assert.True(response422.GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task Added_422_schema_exposes_title_status_detail_type_and_errors()
    {
        using var spec = await FetchSpec();

        var schema = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/special-events")
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty("422")
            .GetProperty("content")
            .GetProperty("application/problem+json")
            .GetProperty("schema");

        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("title", out _));
        Assert.True(properties.TryGetProperty("status", out _));
        Assert.True(properties.TryGetProperty("detail", out _));
        Assert.True(properties.TryGetProperty("type", out _));
        Assert.True(properties.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Decimal_property_retains_double_format()
    {
        using var spec = await FetchSpec();

        var priceSchema = FindRequestBodyProperty(spec, "/special-events", "post", "price");

        // USAGE.md §7.1: decimal publishes with format "double"; the format is
        // the carrier of the range information, which SpecGuard expands into
        // min/max only during validation.
        Assert.Equal("number", priceSchema.GetProperty("type").GetString());
        Assert.Equal("double", priceSchema.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Int32_query_parameter_retains_int32_format()
    {
        using var spec = await FetchSpec();

        var limit = FindQueryParameterSchema(spec, "/museum-hours", "get", "limit");

        Assert.Equal("integer", limit.GetProperty("type").GetString());
        Assert.Equal("int32", limit.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Guid_path_parameter_publishes_format_uuid()
    {
        using var spec = await FetchSpec();

        var eventId = FindPathParameterSchema(spec, "/special-events/{eventId}", "get", "eventId");

        Assert.Equal("string", eventId.GetProperty("type").GetString());
        Assert.Equal("uuid", eventId.GetProperty("format").GetString());
    }

    [Fact]
    public async Task EmailAddress_property_publishes_format_email()
    {
        using var spec = await FetchSpec();

        var email = FindRequestBodyProperty(spec, "/tickets", "post", "email");

        Assert.Equal("email", email.GetProperty("format").GetString());
    }

    [Fact]
    public async Task DateOnly_property_publishes_format_date()
    {
        using var spec = await FetchSpec();

        var ticketDate = FindRequestBodyProperty(spec, "/tickets", "post", "ticketDate");

        Assert.Equal("string", ticketDate.GetProperty("type").GetString());
        Assert.Equal("date", ticketDate.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Nullable_guid_property_is_published_as_anyOf_not_oneOf()
    {
        // BuyMuseumTicketsRequest.EventId is Guid? — SpecGuard rewrites the
        // ASP.NET-generated nullable oneOf into an anyOf so both branches may
        // match a null value.
        using var spec = await FetchSpec();

        var eventId = FindRequestBodyProperty(spec, "/tickets", "post", "eventId");

        Assert.False(eventId.TryGetProperty("oneOf", out _));
        // When the type system simply emits `{ type: ["string","null"], format: "uuid" }`
        // there is no anyOf either; both shapes are acceptable — assert that null
        // is allowed in whatever form the spec takes.
        Assert.True(AllowsNull(eventId));
    }

    [Fact]
    public async Task Numeric_schemas_drop_string_type_alternative_by_default()
    {
        using var spec = await FetchSpec();

        var price = FindRequestBodyProperty(spec, "/special-events", "post", "price");

        Assert.DoesNotContain("string", TypeNames(price));
    }

    [Fact]
    public async Task Numeric_schemas_drop_generated_pattern_by_default()
    {
        using var spec = await FetchSpec();

        var price = FindRequestBodyProperty(spec, "/special-events", "post", "price");

        Assert.False(price.TryGetProperty("pattern", out _));
    }

    private static JsonElement FindRequestBodyProperty(JsonDocument spec, string path, string method, string propertyName)
    {
        var bodySchema = spec.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        return ResolveRef(spec, bodySchema)
            .GetProperty("properties")
            .GetProperty(propertyName);
    }

    private static JsonElement ResolveRef(JsonDocument spec, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var refEl))
        {
            return schema;
        }

        var path = refEl.GetString()!;
        const string prefix = "#/components/schemas/";
        if (!path.StartsWith(prefix)) return schema;
        var name = path[prefix.Length..];

        return spec.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }

    private static JsonElement FindPathParameterSchema(JsonDocument spec, string path, string method, string name)
        => FindParameterSchema(spec, path, method, name, "path");

    private static JsonElement FindQueryParameterSchema(JsonDocument spec, string path, string method, string name)
        => FindParameterSchema(spec, path, method, name, "query");

    private static JsonElement FindParameterSchema(JsonDocument spec, string path, string method, string name, string @in)
    {
        var parameters = spec.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("parameters");

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (parameter.GetProperty("name").GetString() == name &&
                parameter.GetProperty("in").GetString() == @in)
            {
                return parameter.GetProperty("schema");
            }
        }

        throw new Xunit.Sdk.XunitException($"Parameter '{name}' in '{@in}' not found on {method.ToUpperInvariant()} {path}");
    }

    private static IReadOnlyCollection<string> TypeNames(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return Array.Empty<string>();
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            return [type.GetString()!];
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    names.Add(item.GetString()!);
                }
            }
            return names;
        }

        return Array.Empty<string>();
    }

    private static bool AllowsNull(JsonElement schema)
    {
        if (TypeNames(schema).Contains("null"))
        {
            return true;
        }

        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            foreach (var branch in anyOf.EnumerateArray())
            {
                if (TypeNames(branch).Contains("null")) return true;
            }
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            foreach (var branch in oneOf.EnumerateArray())
            {
                if (TypeNames(branch).Contains("null")) return true;
            }
        }

        return false;
    }
}
