using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Test.ValidationResults;

public class ValidationErrorResultTests
{
    [Fact]
    public void Populates_problem_details_with_errors()
    {
        var errors = new[]
        {
            new ValidationErrorResult.ValidationError("name is required", "body", "/name"),
            new ValidationErrorResult.ValidationError("status must be one of [available, pending, sold]", "body", "/status"),
        };

        var result = new ValidationErrorResult(errors);

        Assert.Equal("Validation Failed", result.ProblemDetails.Title);
        Assert.Equal("One or more validation errors occurred", result.ProblemDetails.Detail);
        Assert.Equal(422, result.ProblemDetails.Status);
        Assert.Equal("https://www.rfc-editor.org/rfc/rfc9110#section-15.5.21", result.ProblemDetails.Type);

        var stored = Assert.IsAssignableFrom<IReadOnlyList<ValidationErrorResult.ValidationError>>(
            result.ProblemDetails.Extensions["errors"]);
        Assert.Equal(errors, stored);
    }

    [Fact]
    public void Errors_serialize_with_camelCase_member_names()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var result = new ValidationErrorResult(
            new ValidationErrorResult.ValidationError("m", "body", "/p"));

        var json = JsonSerializer.Serialize(result.ProblemDetails, options);
        using var document = JsonDocument.Parse(json);

        var error = document.RootElement.GetProperty("errors")[0];
        Assert.True(error.TryGetProperty("message", out _));
        Assert.True(error.TryGetProperty("in", out _));
        Assert.True(error.TryGetProperty("path", out _));
    }

    [Fact]
    public void Errors_array_serializes_as_json_array()
    {
        var result = new ValidationErrorResult(
            new ValidationErrorResult.ValidationError("a", "body", "/x"),
            new ValidationErrorResult.ValidationError("b", "query", "y"));

        var json = JsonSerializer.Serialize(result.ProblemDetails);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("errors").ValueKind);
    }

    [Fact]
    public void Empty_path_on_body_error_serializes_as_empty_string_not_omitted()
    {
        var result = new ValidationErrorResult(
            new ValidationErrorResult.ValidationError("body required", "body", ""));

        var json = JsonSerializer.Serialize(result.ProblemDetails);
        using var document = JsonDocument.Parse(json);

        var error = document.RootElement.GetProperty("errors")[0];
        Assert.True(error.TryGetProperty("Path", out var path) || error.TryGetProperty("path", out path));
        Assert.Equal(JsonValueKind.String, path.ValueKind);
        Assert.Equal("", path.GetString());
    }

    [Fact]
    public void Message_with_quotes_and_backslashes_survives_serialization()
    {
        var msg = "line \"A\" \\ line \"B\"";
        var result = new ValidationErrorResult(
            new ValidationErrorResult.ValidationError(msg, "body", ""));

        var json = JsonSerializer.Serialize(result.ProblemDetails);
        using var document = JsonDocument.Parse(json);

        var error = document.RootElement.GetProperty("errors")[0];
        var roundtripped = error.TryGetProperty("Message", out var m) || error.TryGetProperty("message", out m);
        Assert.True(roundtripped);
        Assert.Equal(msg, m.GetString());
    }

    [Fact]
    public void Supports_empty_error_list()
    {
        var result = new ValidationErrorResult();

        var stored = Assert.IsAssignableFrom<IReadOnlyList<ValidationErrorResult.ValidationError>>(
            result.ProblemDetails.Extensions["errors"]);
        Assert.Empty(stored);
    }
}
