using Microsoft.AspNetCore.Mvc;

namespace SpecGuard.Validators.ValidationResults;

internal class ValidationErrorResult
{
    public ProblemDetails ProblemDetails { get; }

    public ValidationErrorResult(params ValidationError[] errors)
    {
        ProblemDetails = new ProblemDetails
        {
            Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.21",
            Title = "Validation Failed",
            Detail = "One or more validation errors occurred",
            Status = 422,
            Extensions =
            {
                ["errors"] = errors.AsReadOnly()
            },
        };
    }

    internal record ValidationError(string Message, string In, string Path);
}
