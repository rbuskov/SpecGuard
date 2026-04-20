using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpecGuard;
using SpecGuard.Sanitizers;

namespace SpecGuard.MuseumApi.Tests;

/// <summary>
/// End-to-end type coverage per USAGE.md §7.1 and §7.2. One test-local
/// minimal host per type family keeps option/type assertions isolated
/// from the shipped sample projects.
/// </summary>
public class ClrTypeCoverageTests
{
    private static async Task<TestServer> StartAsync<TBody>(Func<TBody, IResult> handler)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddOpenApi();
                    services.AddSpecGuard();
                    services.AddRouting();
                    services.AddSingleton<HttpMessageHandler>(sp =>
                        ((TestServer)sp.GetRequiredService<IServer>()).CreateHandler());
                });
                webHost.Configure(app =>
                {
                    app.UseSpecGuard();
                    app.UseRouting();
                    app.UseEndpoints(routes =>
                    {
                        routes.MapOpenApi();
                        routes.MapPost("/items", handler);
                    });
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private sealed record ByteBody(byte V);
    private sealed record SByteBody(sbyte V);
    private sealed record ShortBody(short V);
    private sealed record UShortBody(ushort V);
    private sealed record IntBody(int V);
    private sealed record UIntBody(uint V);
    private sealed record LongBody(long V);
    private sealed record HalfBody(Half V);
    private sealed record FloatBody(float V);
    private sealed record DoubleBody(double V);

    [Fact]
    public async Task Byte_body_rejects_out_of_range_via_format_derived_maximum()
    {
        using var server = await StartAsync<ByteBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        // 256 is outside the uint8 range — SpecGuard's format-derived
        // maximum catches this before the handler runs.
        var response = await client.PostAsJsonAsync("/items", new { v = 256 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task SByte_body_rejects_out_of_range_via_format_derived_minimum()
    {
        using var server = await StartAsync<SByteBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = -129 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Byte_body_accepts_in_range_value()
    {
        using var server = await StartAsync<ByteBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = 200 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Short_body_accepts_in_range_value()
    {
        using var server = await StartAsync<ShortBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = 100 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Int_body_rejects_wrong_type_string()
    {
        using var server = await StartAsync<IntBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = "forty-two" });
        // Either SpecGuard's schema validator (422) or the int deserializer
        // (400) rejects the string value before the handler runs.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    // ── §11.2 string types ────────────────────────────────────────────────

    private sealed record GuidBody(Guid? V);
    private sealed record DateOnlyBody(DateOnly V);
    private sealed record DateTimeOffsetBody(DateTimeOffset V);
    private sealed record UriBody(Uri V);
    private sealed record ByteArrayBody(byte[] V);
    private sealed record TimeSpanBody(TimeSpan V);
    private sealed record DurationTimeSpanBody([property: Duration] TimeSpan V);
    private sealed record TimeOnlyBody(TimeOnly V);
    private sealed record ByteArrayBodyBase64(byte[] V);
    private sealed record DefaultTimeSpanBody(TimeSpan V);

    [Fact]
    public async Task TimeOnly_body_invalid_value_is_rejected()
    {
        using var server = await StartAsync<TimeOnlyBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = "not-a-time" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    [Fact]
    public async Task ByteArray_body_invalid_base64_is_rejected()
    {
        using var server = await StartAsync<ByteArrayBodyBase64>(b => Results.Ok(b));
        var client = server.CreateClient();

        // !! includes characters not in the base64 alphabet.
        var response = await client.PostAsJsonAsync("/items", new { v = "!!" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    [Fact]
    public async Task Default_TimeSpan_body_invalid_value_is_rejected()
    {
        using var server = await StartAsync<DefaultTimeSpanBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = "not-a-timespan" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    [Fact]
    public async Task Guid_body_invalid_value_is_rejected()
    {
        using var server = await StartAsync<GuidBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = "not-a-guid" });

        // Either SpecGuard's `format: uuid` check fires (422) or the
        // framework's Guid deserializer fails (400). Both confirm
        // rejection before the handler runs.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    [Fact]
    public async Task Uri_body_invalid_value_produces_400()
    {
        using var server = await StartAsync<UriBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        // System.Text.Json's Uri deserializer accepts most strings; a schema
        // check for invalid Uri format would fire as 422. Either shape
        // (400 parse fail or 422 validation fail) confirms the value is
        // rejected.
        var response = await client.PostAsJsonAsync("/items", new { v = "" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    [Fact]
    public async Task DateTimeOffset_body_invalid_value_produces_400_or_422()
    {
        using var server = await StartAsync<DateTimeOffsetBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = "not-a-date" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    [Fact]
    public async Task Duration_TimeSpan_accepts_iso8601_via_handler()
    {
        using var server = await StartAsync<DurationTimeSpanBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = "PT1H30M" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Duration_TimeSpan_rejects_malformed_iso8601()
    {
        using var server = await StartAsync<DurationTimeSpanBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/items", new { v = "not-a-duration" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    }

    [Fact]
    public async Task Duration_TimeSpan_publishes_format_duration_without_default_pattern()
    {
        using var server = await StartAsync<DurationTimeSpanBody>(b => Results.Ok(b));
        var client = server.CreateClient();

        using var spec = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        // The request body schema is a $ref into components.
        var body = spec.RootElement
            .GetProperty("paths").GetProperty("/items").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        var resolved = ResolveRef(spec, body);
        var v = resolved.GetProperty("properties").GetProperty("v");

        Assert.Equal("string", v.GetProperty("type").GetString());
        Assert.Equal("duration", v.GetProperty("format").GetString());
        Assert.False(v.TryGetProperty("pattern", out _));
    }

    private static JsonElement ResolveRef(JsonDocument spec, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var refEl)) return schema;
        var path = refEl.GetString()!;
        const string prefix = "#/components/schemas/";
        if (!path.StartsWith(prefix)) return schema;
        return spec.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(path[prefix.Length..]);
    }
}
