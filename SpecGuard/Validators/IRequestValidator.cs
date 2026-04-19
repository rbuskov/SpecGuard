using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Validators;

internal interface IRequestValidator
{
    void Initialize(JsonDocument openApiSpec);

    ValueTask<IReadOnlyList<ValidationErrorResult.ValidationError>> ValidateAsync(HttpContext context, CancellationToken cancellationToken);
}
