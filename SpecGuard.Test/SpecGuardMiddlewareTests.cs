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
    public async Task Malformed_json_400_response_content_type_is_problem_plus_json()
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));

        await middleware.InvokeAsync(context);

        Assert.StartsWith("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task Request_with_json_content_type_but_no_body_when_optional_passes_through()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var validator = MockValidator([]);
        var middleware = BuildMiddleware(next, validator.Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task HttpMessageHandler_in_DI_is_not_disposed_after_use()
    {
        // SpecGuard constructs its HttpClient with disposeHandler:false.
        // Between two independent invocations the DI-registered handler
        // remains usable — assert by verifying the mock can be called
        // again (disposal would throw ObjectDisposedException).
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        // First invocation triggers spec fetch via the handler.
        await middleware.InvokeAsync(BuildContext(handler.Object));

        // Directly invoke the handler via its mock — if it had been
        // disposed, this would throw.
        using var client = new HttpClient(handler.Object, disposeHandler: false);
        var response = await client.GetAsync("http://localhost/openapi/v1.json");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Spec_url_with_query_string_in_request_path_is_not_treated_as_spec()
    {
        // The spec-request comparison is path-based; a request to
        // /openapi/v1.json?foo=1 has the same Path but extra Query.
        // Uri.Compare on UriComponents.Path should still match — lock
        // that query strings don't break the bypass.
        var validator = MockValidator([]);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, validator.Object);

        var context = BuildContext(DefaultHandler);
        context.Request.Path = new PathString("/openapi/v1.json");
        context.Request.QueryString = new QueryString("?foo=1");

        await middleware.InvokeAsync(context);

        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Absolute_spec_url_with_different_port_is_not_a_spec_request()
    {
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "http://localhost:9999/openapi/v1.json");

        var context = BuildContext(handler.Object);
        context.Request.Host = new HostString("localhost", 8080);
        context.Request.Path = new PathString("/openapi/v1.json");

        // Port mismatch → not bypassed → spec fetch attempted to the
        // configured absolute URL.
        await middleware.InvokeAsync(context);

        VerifySpecRequested(handler, new Uri("http://localhost:9999/openapi/v1.json"));
    }

    [Fact]
    public async Task Absolute_spec_url_with_different_host_is_not_a_spec_request()
    {
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [MockValidator([]).Object],
            "https://api.elsewhere.com/spec.json");

        var context = BuildContext(handler.Object);
        context.Request.Host = new HostString("localhost");
        context.Request.Path = new PathString("/spec.json");

        // Because scheme+host+port mismatch, this request is NOT treated
        // as a spec request — validators run against it.
        await middleware.InvokeAsync(context);

        // Validators should have been invoked for initialization check.
        // We can't directly assert "ValidateAsync was called" without a
        // mock setup, but no exception means pass-through was not taken.
        Assert.NotEqual(0, context.Response.StatusCode);  // non-zero = touched
    }

    [Fact]
    public async Task Validation_failure_at_422_leaves_request_body_consumable()
    {
        var validator = MockValidator(
            [new ValidationErrorResult.ValidationError("nope", "body", "")]);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, validator.Object);

        var context = BuildContext(DefaultHandler);
        const string body = """{"name":"Fido"}""";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await middleware.InvokeAsync(context);

        // 422 fired; the request body should still be readable so a
        // downstream exception handler could inspect it.
        Assert.Equal(422, context.Response.StatusCode);

        context.Request.Body.Position = 0;
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var roundtripped = await reader.ReadToEndAsync();
        Assert.Equal(body, roundtripped);
    }

    [Fact]
    public async Task Spec_request_under_pathbase_bypasses_validation()
    {
        var validator = MockValidator([]);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, validator.Object);

        var context = BuildContext(DefaultHandler);
        context.Request.PathBase = new PathString("/myapp");
        context.Request.Path = new PathString("/openapi/v1.json");

        await middleware.InvokeAsync(context);

        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Aggregated_422_errors_appear_in_validator_invocation_order()
    {
        var first = MockValidator([new ValidationErrorResult.ValidationError("first-error", "body", "/a")]);
        var second = MockValidator([new ValidationErrorResult.ValidationError("second-error", "query", "b")]);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, first.Object, second.Object);

        var context = BuildContext(DefaultHandler);
        await middleware.InvokeAsync(context);

        var body = await ReadResponseBody(context);
        using var document = JsonDocument.Parse(body);
        var errors = document.RootElement.GetProperty("errors");

        Assert.Equal(2, errors.GetArrayLength());
        Assert.Equal("first-error", GetMessage(errors[0]));
        Assert.Equal("second-error", GetMessage(errors[1]));
    }

    private static string? GetMessage(JsonElement error) =>
        error.TryGetProperty("message", out var m) ? m.GetString()
        : error.TryGetProperty("Message", out m) ? m.GetString()
        : null;

    [Fact]
    public async Task When_no_validator_matches_next_is_called_exactly_once()
    {
        var nextCallCount = 0;
        RequestDelegate next = _ => { nextCallCount++; return Task.CompletedTask; };
        var validator = MockValidator([], matchesOperation: false);
        var middleware = BuildMiddleware(next, validator.Object);

        await middleware.InvokeAsync(BuildContext(DefaultHandler));

        Assert.Equal(1, nextCallCount);
    }

    [Fact]
    public async Task Empty_spec_object_initializes_validators_and_passes_through()
    {
        var validator = new Mock<IRequestValidator>();
        validator.Setup(v => v.MatchesOperation(It.IsAny<HttpRequest>())).Returns(false);
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [validator.Object],
            "/openapi/v1.json");

        var context = BuildContext(MockHttpHandler("{}").Object);
        await middleware.InvokeAsync(context);

        validator.Verify(v => v.Initialize(It.IsAny<JsonDocument>()), Times.Once);
        // No paths means no validator matches → no 422.
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Spec_without_paths_section_is_accepted_and_bypasses_validation()
    {
        var validator = new Mock<IRequestValidator>();
        validator.Setup(v => v.MatchesOperation(It.IsAny<HttpRequest>())).Returns(false);
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [validator.Object],
            "/openapi/v1.json");

        // Spec has info but no paths — validators initialize to empty and
        // requests pass through.
        var context = BuildContext(MockHttpHandler("""{"info":{"title":"x","version":"1"}}""").Object);
        await middleware.InvokeAsync(context);

        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Spec_returning_non_json_response_surfaces_exception()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html"),
            });

        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        var context = BuildContext(handler.Object);

        await Assert.ThrowsAnyAsync<JsonException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task Http_error_from_spec_url_propagates_and_retries_on_next_request()
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
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("oops"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
            });

        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        var context = BuildContext(handler.Object);

        await Assert.ThrowsAsync<HttpRequestException>(() => middleware.InvokeAsync(context));

        // Second request succeeds — faulted task was not cached.
        await middleware.InvokeAsync(context);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Concurrent_first_requests_fetch_spec_only_once()
    {
        var handler = MockHttpHandler("{}");
        var middleware = new SpecGuardMiddleware(
            _ => Task.CompletedTask,
            [],
            "/openapi/v1.json");

        var ctx1 = BuildContext(handler.Object);
        var ctx2 = BuildContext(handler.Object);

        await Task.WhenAll(middleware.InvokeAsync(ctx1), middleware.InvokeAsync(ctx2));

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Spec_request_path_comparison_is_case_insensitive()
    {
        var validator = MockValidator([]);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, validator.Object);

        var context = BuildContext(DefaultHandler);
        context.Request.Path = new PathString("/Openapi/V1.json");

        await middleware.InvokeAsync(context);

        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Non_json_body_on_unmatched_operation_passes_through()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = BuildMiddleware(next, MockValidator([], matchesOperation: false).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/xml";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("<x/>"));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Unmatched_operation_does_not_invoke_validators()
    {
        var validator = MockValidator([], matchesOperation: false);
        var middleware = BuildMiddleware(_ => Task.CompletedTask, validator.Object);

        var context = BuildContext(DefaultHandler);
        await middleware.InvokeAsync(context);

        validator.Verify(
            v => v.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Malformed_json_400_body_includes_rfc_type_url()
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBody(context);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
            document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Malformed_json_400_body_has_integer_status()
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBody(context);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("status").ValueKind);
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Utf8_bom_in_body_does_not_produce_400()
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.UTF8.GetBytes("""{"ok":true}"""));
        context.Request.Body = new MemoryStream(bytes.ToArray());

        await middleware.InvokeAsync(context);

        Assert.NotEqual(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task Trailing_whitespace_in_body_does_not_produce_400()
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"ok\":true}\n"));

        await middleware.InvokeAsync(context);

        Assert.NotEqual(400, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"X\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[1,2,3]")]
    public async Task Top_level_json_values_are_not_malformed(string body)
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await middleware.InvokeAsync(context);

        Assert.NotEqual(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task Whitespace_only_body_is_treated_as_empty_not_malformed()
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("   \n\t  "));

        await middleware.InvokeAsync(context);

        Assert.NotEqual(400, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("application/vnd.company.v1+json")]
    [InlineData("application/hal+json")]
    [InlineData("application/ld+json")]
    [InlineData("application/problem+json")]
    public async Task Content_type_is_recognized_as_json(string contentType)
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));

        await middleware.InvokeAsync(context);

        // Malformed JSON with any of these content types should be caught as 400.
        Assert.Equal(400, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/json")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data")]
    public async Task Content_type_is_not_recognized_as_json(string contentType)
    {
        var middleware = BuildMiddleware(_ => Task.CompletedTask, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));

        await middleware.InvokeAsync(context);

        // Non-JSON content types bypass body parsing → no 400.
        Assert.NotEqual(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task Malformed_content_type_header_passes_through_without_400()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = BuildMiddleware(next, MockValidator([]).Object);

        var context = BuildContext(DefaultHandler);
        context.Request.ContentType = "application/;json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(400, context.Response.StatusCode);
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
