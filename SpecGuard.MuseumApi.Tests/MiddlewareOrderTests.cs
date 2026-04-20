using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
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
/// Integration tests for the middleware-ordering caveats in USAGE.md §6.
/// Each test stands up its own minimal host via TestServer so the shipped
/// sample Program.cs files are untouched.
/// </summary>
public class MiddlewareOrderTests
{
    private sealed record Widget(string Name);

    private sealed class HostBuilder
    {
        private readonly List<Action<IApplicationBuilder>> pre = [];
        private readonly List<Action<IApplicationBuilder>> post = [];
        private readonly List<Action<IEndpointRouteBuilder>> endpoints = [];

        public HostBuilder Before(Action<IApplicationBuilder> configure)
        {
            pre.Add(configure);
            return this;
        }

        public HostBuilder After(Action<IApplicationBuilder> configure)
        {
            post.Add(configure);
            return this;
        }

        public HostBuilder MapEndpoints(Action<IEndpointRouteBuilder> map)
        {
            endpoints.Add(map);
            return this;
        }

        public async Task<TestServer> StartAsync()
        {
            var host = await new HostBuilder1()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddOpenApi();
                        services.AddSpecGuard();
                        services.AddRouting();
                        // SpecGuard fetches the spec from the running host.
                        // TestServer doesn't listen on TCP — hand SpecGuard
                        // the in-process handler instead.
                        services.AddSingleton<HttpMessageHandler>(sp =>
                            sp.GetRequiredService<IServer>() is TestServer ts
                                ? ts.CreateHandler()
                                : throw new InvalidOperationException("Expected TestServer."));
                    });
                    webHost.Configure(app =>
                    {
                        foreach (var p in pre) p(app);
                        app.UseSpecGuard();
                        foreach (var p in post) p(app);
                        app.UseRouting();
                        app.UseEndpoints(routes =>
                        {
                            routes.MapOpenApi();
                            foreach (var map in endpoints) map(routes);
                        });
                    });
                })
                .StartAsync();

            return host.GetTestServer();
        }
    }

    // Type-alias helper because Microsoft.Extensions.Hosting.HostBuilder and
    // our fixture helper share the same unqualified name.
    private sealed class HostBuilder1 : Microsoft.Extensions.Hosting.HostBuilder { }

    [Fact]
    public async Task Auth_before_SpecGuard_short_circuits_anonymous_requests_without_validation()
    {
        // Authn/authz before SpecGuard: anonymous request to a protected
        // endpoint is rejected by auth (401/403), and SpecGuard's body
        // validation never runs.
        using var server = await new HostBuilder()
            .Before(app =>
            {
                app.Use(async (ctx, next) =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/widgets") &&
                        !ctx.Request.Headers.ContainsKey("X-Auth"))
                    {
                        ctx.Response.StatusCode = 401;
                        return;
                    }
                    await next();
                });
            })
            .MapEndpoints(routes => routes.MapPost("/widgets",
                (Widget w) => Results.Ok(w)))
            .StartAsync();

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync("/widgets", new { wrong = "field" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_after_SpecGuard_validates_anonymous_requests_and_returns_422()
    {
        // Auth after SpecGuard: anonymous request with invalid body is
        // caught by SpecGuard (422), not by auth. We scope the auth check
        // to /widgets so the spec-fetch pipeline (which routes through
        // /openapi/v1.json) isn't blocked.
        using var server = await new HostBuilder()
            .After(app =>
            {
                app.Use(async (ctx, next) =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/widgets") &&
                        !ctx.Request.Headers.ContainsKey("X-Auth"))
                    {
                        ctx.Response.StatusCode = 401;
                        return;
                    }
                    await next();
                });
            })
            .MapEndpoints(routes => routes.MapPost("/widgets",
                (Widget w) => Results.Ok(w)))
            .StartAsync();

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync("/widgets", new { wrong = "field" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Exception_handler_after_SpecGuard_does_not_rewrite_the_422()
    {
        using var server = await new HostBuilder()
            .After(app => app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async ctx =>
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync("rewritten");
                });
            }))
            .MapEndpoints(routes => routes.MapPost("/widgets",
                (Widget w) => Results.Ok(w)))
            .StartAsync();

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync("/widgets", new { wrong = "field" });

        // An empty body (required field `name` missing) must surface as 422,
        // not get rewritten by the downstream exception handler.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Path_rewrite_after_SpecGuard_does_not_affect_matching()
    {
        using var server = await new HostBuilder()
            .After(app => app.Use((ctx, next) =>
            {
                // Rewrite /widgets → /whatever, which has no spec entry.
                // SpecGuard already ran against /widgets, so validation must
                // fire; the rewrite only redirects the eventually-executed
                // endpoint.
                if (ctx.Request.Path == "/widgets")
                {
                    ctx.Request.Path = "/whatever";
                }
                return next();
            }))
            .MapEndpoints(routes =>
            {
                routes.MapPost("/widgets", (Widget w) => Results.Ok(w));
                routes.MapPost("/whatever", () => Results.Ok("rewritten"));
            })
            .StartAsync();

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync("/widgets", new { wrong = "field" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Body_reading_middleware_before_SpecGuard_without_buffering_leaves_body_empty()
    {
        // Simulate a poorly-behaved earlier middleware that reads the request
        // body without enabling buffering. SpecGuard then sees an empty body;
        // a required-body endpoint must surface a "required body missing"
        // error.
        using var server = await new HostBuilder()
            .Before(app => app.Use(async (ctx, next) =>
            {
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                _ = await reader.ReadToEndAsync();
                await next();
            }))
            .MapEndpoints(routes => routes.MapPost("/widgets",
                (Widget w) => Results.Ok(w)))
            .StartAsync();

        var client = server.CreateClient();
        var body = new StringContent("""{"name":"acme"}""", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/widgets", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Body_reading_middleware_before_SpecGuard_with_buffering_preserves_validation()
    {
        // Inverse of the previous test: a middleware that uses EnableBuffering
        // before reading lets SpecGuard see the body and the valid request
        // flows through.
        using var server = await new HostBuilder()
            .Before(app => app.Use(async (ctx, next) =>
            {
                ctx.Request.EnableBuffering();
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                _ = await reader.ReadToEndAsync();
                ctx.Request.Body.Position = 0;
                await next();
            }))
            .MapEndpoints(routes => routes.MapPost("/widgets",
                (Widget w) => Results.Ok(w)))
            .StartAsync();

        var client = server.CreateClient();
        var body = new StringContent("""{"name":"acme"}""", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/widgets", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
