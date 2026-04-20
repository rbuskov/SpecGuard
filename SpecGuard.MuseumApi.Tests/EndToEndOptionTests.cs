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

namespace SpecGuard.MuseumApi.Tests;

/// <summary>
/// End-to-end tests that toggle <see cref="SpecGuardOptions"/> values via
/// test-local minimal hosts. The shipped sample apps register defaults
/// only — any non-default option coverage must live here.
/// </summary>
public class EndToEndOptionTests
{
    private sealed record Widget(string Name, int Count);

    private static async Task<TestServer> StartAsync(Action<SpecGuardOptions> configureOptions)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddOpenApi();
                    services.AddSpecGuard(configureOptions);
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
                        routes.MapPost("/widgets", (Widget w) => Results.Ok(w));
                    });
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task RejectAdditionalProperties_true_rejects_unknown_field()
    {
        using var server = await StartAsync(o => o.RejectAdditionalProperties = true);
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/widgets", new { name = "Acme", count = 1, extra = 42 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectAdditionalProperties_true_accepts_known_fields_only()
    {
        using var server = await StartAsync(o => o.RejectAdditionalProperties = true);
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/widgets", new { name = "Acme", count = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowStringNumerics_true_accepts_string_encoded_number()
    {
        using var server = await StartAsync(o => o.AllowStringNumerics = true);
        var client = server.CreateClient();

        // Send `count` as a string — SpecGuard coerces and validates.
        var response = await client.PostAsJsonAsync("/widgets",
            new { name = "Acme", count = "1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowStringNumerics_false_rejects_string_encoded_number()
    {
        using var server = await StartAsync(_ => { });
        var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/widgets",
            new { name = "Acme", count = "1" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddValidationResponses_false_suppresses_400_and_422_in_spec()
    {
        using var server = await StartAsync(o => o.AddValidationResponses = false);
        var client = server.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using var spec = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responses = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/widgets")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.False(responses.TryGetProperty("400", out _));
        Assert.False(responses.TryGetProperty("422", out _));
    }

    [Fact]
    public async Task Hand_authored_400_is_not_overwritten_by_transformer()
    {
        // Host with a hand-authored 400 response — SpecGuard must not
        // clobber it, but should still add 422.
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
                        routes.MapPost("/widgets", (Widget w) => Results.Ok(w))
                            .ProducesProblem(400, "application/vnd.my-custom+json");
                    });
                });
            })
            .StartAsync();

        using var server = host.GetTestServer();
        var client = server.CreateClient();

        using var spec = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json"));

        var response400 = spec.RootElement
            .GetProperty("paths").GetProperty("/widgets").GetProperty("post")
            .GetProperty("responses").GetProperty("400");

        // Hand-authored media type survives — not replaced by the
        // SpecGuard-added `application/problem+json`.
        var content = response400.GetProperty("content");
        Assert.True(content.TryGetProperty("application/vnd.my-custom+json", out _));
    }

    [Fact]
    public async Task AddValidationResponses_true_default_adds_400_and_422_in_spec()
    {
        using var server = await StartAsync(_ => { });
        var client = server.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        using var spec = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var responses = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/widgets")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("422", out _));
    }
}
