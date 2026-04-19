using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using SpecGuard.Validators;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Test;

public class SpecGuardMiddlewareTests
{
    [Fact]
    public async Task AllValidatorsSucceed_calls_next()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var validator = MockValidator([]);
        var middleware = BuildMiddleware(next, validator.Object);

        var context = BuildContext(DefaultHandler);
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task MalformedJsonBody_short_circuits_and_writes_problem_details()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var validator = MockValidator([]);
        var middleware = BuildMiddleware(next, validator.Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not valid json"));

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(400, context.Response.StatusCode);

        var body = await ReadResponseBody(context);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Malformed JSON", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Unmatched_path_passes_through_even_with_malformed_json_body()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var validator = MockValidator([], matchesOperation: false);
        var middleware = BuildMiddleware(next, validator.Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not valid json"));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AllValidators_run_and_errors_are_aggregated()
    {
        var firstErrors = new List<ValidationErrorResult.ValidationError>
        {
            new("name is required", "body", "/name"),
        };
        var secondErrors = new List<ValidationErrorResult.ValidationError>
        {
            new("limit must be positive", "query", "limit"),
        };
        var first = MockValidator(firstErrors);
        var second = MockValidator(secondErrors);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, first.Object, second.Object);

        var context = BuildContext(DefaultHandler);
        await middleware.InvokeAsync(context);

        first.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
        second.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Equal(422, context.Response.StatusCode);

        var body = await ReadResponseBody(context);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Validation Failed", document.RootElement.GetProperty("title").GetString());

        var errors = document.RootElement.GetProperty("errors");
        Assert.Equal(2, errors.GetArrayLength());
    }

    [Fact]
    public async Task AllValidators_are_invoked_when_each_succeeds()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var first = MockValidator([]);
        var second = MockValidator([]);
        var middleware = BuildMiddleware(next, first.Object, second.Object);

        await middleware.InvokeAsync(BuildContext(DefaultHandler));

        first.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
        second.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task First_invocation_loads_spec_from_relative_url_resolved_against_request_host()
    {
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        await middleware.InvokeAsync(BuildContext(handler.Object));

        VerifySpecRequested(handler, new Uri("http://localhost/openapi/v1.json"));
    }

    [Fact]
    public async Task Spec_is_loaded_only_once_across_invocations()
    {
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        var context = BuildContext(handler.Object);
        await middleware.InvokeAsync(context);
        await middleware.InvokeAsync(context);
        await middleware.InvokeAsync(context);

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task First_invocation_initializes_all_validators_with_parsed_spec()
    {
        var first = MockValidator([]);
        var second = MockValidator([]);
        JsonDocument? capturedFirst = null;
        JsonDocument? capturedSecond = null;
        first.Setup(v => v.Initialize(It.IsAny<JsonDocument>()))
            .Callback<JsonDocument>(d => capturedFirst = d);
        second.Setup(v => v.Initialize(It.IsAny<JsonDocument>()))
            .Callback<JsonDocument>(d => capturedSecond = d);

        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [first.Object, second.Object],
            "/openapi/v1.json");

        var context = BuildContext(MockHttpHandler("{\"openapi\":\"3.1.0\"}").Object);
        await middleware.InvokeAsync(context);
        await middleware.InvokeAsync(context);

        first.Verify(v => v.Initialize(It.IsAny<JsonDocument>()), Times.Once);
        second.Verify(v => v.Initialize(It.IsAny<JsonDocument>()), Times.Once);
        Assert.NotNull(capturedFirst);
        Assert.Equal("3.1.0", capturedFirst!.RootElement.GetProperty("openapi").GetString());
        Assert.Same(capturedFirst, capturedSecond);
    }

    [Fact]
    public async Task Relative_spec_url_is_resolved_against_path_base()
    {
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        var context = BuildContext(handler.Object);
        context.Request.PathBase = new PathString("/myapp");
        await middleware.InvokeAsync(context);

        VerifySpecRequested(handler, new Uri("http://localhost/myapp/openapi/v1.json"));
    }

    [Fact]
    public async Task Spec_request_passes_through_for_relative_spec_url()
    {
        var validator = MockValidator([]);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, validator.Object);

        var context = BuildContext(DefaultHandler);
        context.Request.Path = new PathString("/openapi/v1.json");

        await middleware.InvokeAsync(context);

        validator.Verify(v => v.Initialize(It.IsAny<JsonDocument>()), Times.Never);
        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Spec_request_passes_through_for_absolute_spec_url_on_same_host()
    {
        var validator = MockValidator([]);
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [validator.Object],
            "http://localhost/openapi/v1.json");

        var context = BuildContext(DefaultHandler);
        context.Request.Path = new PathString("/openapi/v1.json");

        await middleware.InvokeAsync(context);

        validator.Verify(v => v.Initialize(It.IsAny<JsonDocument>()), Times.Never);
        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Absolute_spec_url_is_used_verbatim()
    {
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "https://example.com/spec.json");

        await middleware.InvokeAsync(BuildContext(handler.Object));

        VerifySpecRequested(handler, new Uri("https://example.com/spec.json"));
    }

    [Fact]
    public async Task Spec_load_failure_does_not_cache_faulted_task_and_retries_on_next_request()
    {
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new HttpRequestException("connection refused");
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };
            });

        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        var context = BuildContext(handler.Object);

        // First request fails — spec unreachable.
        await Assert.ThrowsAsync<HttpRequestException>(() => middleware.InvokeAsync(context));

        // Second request retries and succeeds.
        await middleware.InvokeAsync(context);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Json_body_stream_is_rewound_for_downstream_handler()
    {
        const string body = """{"name":"Fido"}""";
        string? downstreamBody = null;
        RequestDelegate next = async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            downstreamBody = await reader.ReadToEndAsync();
        };
        var middleware = BuildMiddleware(next, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await middleware.InvokeAsync(context);

        Assert.Equal(body, downstreamBody);
    }

    [Fact]
    public async Task Large_json_body_is_rewound_for_downstream_handler()
    {
        // Exceed the default in-memory buffering threshold (~30 KB) to force
        // FileBufferingReadStream to spool to disk and still rewind.
        var payload = new string('x', 64 * 1024);
        var body = $$"""{"name":"{{payload}}"}""";
        string? downstreamBody = null;
        RequestDelegate next = async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            downstreamBody = await reader.ReadToEndAsync();
        };
        var middleware = BuildMiddleware(next, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await middleware.InvokeAsync(context);

        Assert.Equal(body, downstreamBody);
    }

    [Fact]
    public async Task Non_json_body_is_not_consumed()
    {
        const string body = "<xml />";
        string? downstreamBody = null;
        RequestDelegate next = async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            downstreamBody = await reader.ReadToEndAsync();
        };
        var middleware = BuildMiddleware(next, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/xml";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await middleware.InvokeAsync(context);

        Assert.Equal(body, downstreamBody);
    }

    [Fact]
    public async Task Unmatched_operation_does_not_consume_body()
    {
        const string body = """{"name":"Fido"}""";
        string? downstreamBody = null;
        RequestDelegate next = async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            downstreamBody = await reader.ReadToEndAsync();
        };
        var middleware = BuildMiddleware(next, MockValidator([], matchesOperation: false).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await middleware.InvokeAsync(context);

        Assert.Equal(body, downstreamBody);
    }

    private static readonly HttpMessageHandler DefaultHandler = MockHttpHandler("{}").Object;

    private static SpecGuardMiddleware BuildMiddleware(RequestDelegate next, params IRequestValidator[] validators) =>
        new(next, validators, "/openapi/v1.json");

    private static Mock<IRequestValidator> MockValidator(IReadOnlyList<ValidationErrorResult.ValidationError> errors, bool matchesOperation = true)
    {
        var mock = new Mock<IRequestValidator>();
        mock.Setup(v => v.MatchesOperation(It.IsAny<HttpRequest>())).Returns(matchesOperation);
        mock.Setup(v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(errors);
        return mock;
    }

    private static Mock<HttpMessageHandler> MockHttpHandler(string jsonBody)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
            });
        return handler;
    }

    private static void VerifySpecRequested(Mock<HttpMessageHandler> handler, Uri expectedUri)
    {
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.RequestUri == expectedUri),
            ItExpr.IsAny<CancellationToken>());
    }

    private static DefaultHttpContext BuildContext(HttpMessageHandler? handler = null)
    {
        var services = new ServiceCollection();
        if (handler is not null)
            services.AddSingleton(handler);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Response.Body = new MemoryStream();
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    private static async Task<string> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
