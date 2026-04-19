using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SpecGuard.Validators;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Test;

public class ExtensionMethodsTests
{
    [Fact]
    public void AddRequestValidation_registers_builtin_validators()
    {
        var services = new ServiceCollection();

        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IRequestValidator>().ToArray();

        Assert.Contains(validators, v => v is ParameterValidator);
        Assert.Contains(validators, v => v is JsonBodyValidator);
    }

    [Fact]
    public void AddRequestValidation_is_idempotent()
    {
        var services = new ServiceCollection();

        services.AddSpecGuard();
        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IRequestValidator>().ToArray();

        Assert.Equal(1, validators.Count(v => v is ParameterValidator));
        Assert.Equal(1, validators.Count(v => v is JsonBodyValidator));
    }

    [Fact]
    public void AddRequestValidation_preserves_user_registered_validators()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestValidator, CustomValidator>();

        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IRequestValidator>().ToArray();

        Assert.Contains(validators, v => v is CustomValidator);
        Assert.Contains(validators, v => v is ParameterValidator);
        Assert.Contains(validators, v => v is JsonBodyValidator);
    }

    private sealed class CustomValidator : IRequestValidator
    {
        public void Initialize(JsonDocument openApiSpec)
        {
        }

        public ValueTask<IReadOnlyList<ValidationErrorResult.ValidationError>> ValidateAsync(HttpContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ValidationErrorResult.ValidationError>>(Array.Empty<ValidationErrorResult.ValidationError>());
    }
}
