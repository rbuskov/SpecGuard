using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SpecGuard.Validators;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard;

internal sealed class SpecGuardMiddleware(
    RequestDelegate next,
    IEnumerable<IRequestValidator> validators,
    string specUrl)
{
    internal const string ParsedBodyKey = "SpecGuard.ParsedBody";
    internal const string BodyEmptyKey = "SpecGuard.BodyEmpty";
    private const string ProblemJsonContentType = "application/problem+json";

    private readonly IRequestValidator[] validators = validators.ToArray();
    private readonly Lock specLock = new();
    private Task? initializationTask;
    private JsonDocument? openApiSpec;

    private static async Task WriteProblemJsonAsync(HttpContext context, int status, ProblemDetails details)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = ProblemJsonContentType;

        var jsonOptions = context.RequestServices
            .GetService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            ?.Value.SerializerOptions;

        await JsonSerializer.SerializeAsync(
            context.Response.Body, details, details.GetType(), jsonOptions);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsSpecRequest(context))
        {
            await next(context);
            return;
        }

        await EnsureInitializedAsync(context);

        // Paths not described in the spec pass through untouched — SpecGuard
        // only asserts behavior on operations the spec actually declares.
        if (!IsRequestDescribedInSpec(context.Request))
        {
            await next(context);
            return;
        }

        // Step 1: If the request carries a JSON body, parse it upfront.
        // Malformed JSON is a 400 — we bail out before running any validators.
        if (IsJsonRequest(context.Request))
        {
            var parseResult = await ParseRequestBody(context.Request);

            if (parseResult.MalformedMessage is not null)
            {
                var details = new ProblemDetails
                {
                    Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
                    Title = "Malformed JSON",
                    Detail = parseResult.MalformedMessage,
                    Status = 400,
                };
                await WriteProblemJsonAsync(context, 400, details);
                return;
            }

            if (parseResult.IsEmpty)
            {
                context.Items[BodyEmptyKey] = true;
            }
            else if (parseResult.Body is { } body)
            {
                context.Items[ParsedBodyKey] = body;
            }
        }

        // Step 2: Run ALL validators and collect every error.
        var allErrors = new List<ValidationErrorResult.ValidationError>();

        foreach (var validator in validators)
        {
            var errors = await validator.ValidateAsync(context, context.RequestAborted);
            allErrors.AddRange(errors);
        }

        if (allErrors.Count > 0)
        {
            var result = new ValidationErrorResult(allErrors.ToArray());
            await WriteProblemJsonAsync(context, 422, result.ProblemDetails);
            return;
        }

        await next(context);
    }

    private bool IsRequestDescribedInSpec(HttpRequest request)
    {
        foreach (var validator in validators)
        {
            if (validator.MatchesOperation(request))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJsonRequest(HttpRequest request)
    {
        if (request.ContentType is null)
            return false;

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType))
            return false;

        var type = mediaType.MediaType.Value;
        if (type is null)
            return false;

        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
               || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ParseResult> ParseRequestBody(HttpRequest request)
    {
        try
        {
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var bodyString = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(bodyString))
            {
                return ParseResult.EmptyBody;
            }

            return new ParseResult(JsonElement.Parse(bodyString));
        }
        catch (JsonException ex)
        {
            return new ParseResult(MalformedMessage: ex.Message);
        }
        finally
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }
        }
    }

    private readonly record struct ParseResult(
        JsonElement? Body = null,
        string? MalformedMessage = null,
        bool IsEmpty = false)
    {
        public static readonly ParseResult EmptyBody = new(IsEmpty: true);
    }

    private Task EnsureInitializedAsync(HttpContext context)
    {
        var task = initializationTask;
        if (task is not null && !task.IsFaulted)
        {
            return task;
        }

        lock (specLock)
        {
            if (initializationTask is not null && !initializationTask.IsFaulted)
            {
                return initializationTask;
            }

            return initializationTask = LoadAndInitializeAsync(ResolveSpecUri(context), context.RequestServices);
        }
    }

    private async Task LoadAndInitializeAsync(Uri specUri, IServiceProvider services)
    {
        var handler = services.GetService<HttpMessageHandler>();
        using var client = handler is not null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
        await using var stream = await client.GetStreamAsync(specUri);
        openApiSpec = await JsonDocument.ParseAsync(stream);

        foreach (var validator in validators)
        {
            validator.Initialize(openApiSpec);
        }
    }

    private Uri ResolveSpecUri(HttpContext context)
    {
        if (TryGetAbsoluteHttpUri(out var absolute))
        {
            return absolute;
        }

        var request = context.Request;
        var path = request.PathBase.Add(specUrl);
        return new Uri($"{request.Scheme}://{request.Host}{path}");
    }

    private bool IsSpecRequest(HttpContext context)
    {
        var request = context.Request;
        var requestPath = request.PathBase.Add(request.Path);
        var requestUri = new Uri($"{request.Scheme}://{request.Host}{requestPath}");
        var specUri = ResolveSpecUri(context);

        return Uri.Compare(
            requestUri,
            specUri,
            UriComponents.Scheme | UriComponents.HostAndPort | UriComponents.Path,
            UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    private bool TryGetAbsoluteHttpUri(out Uri absolute)
    {
        if (Uri.TryCreate(specUrl, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            absolute = parsed;
            return true;
        }

        absolute = null!;
        return false;
    }
}
