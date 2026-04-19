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
    public void Supports_empty_error_list()
    {
        var result = new ValidationErrorResult();

        var stored = Assert.IsAssignableFrom<IReadOnlyList<ValidationErrorResult.ValidationError>>(
            result.ProblemDetails.Extensions["errors"]);
        Assert.Empty(stored);
    }
}
