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

    [Fact]
    public void AddSpecGuard_registers_the_options_singleton()
    {
        var services = new ServiceCollection();

        services.AddSpecGuard(o =>
        {
            o.RejectAdditionalProperties = true;
            o.AllowStringNumerics = true;
            o.AddValidationResponses = false;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SpecGuardOptions>();

        Assert.True(options.RejectAdditionalProperties);
        Assert.True(options.AllowStringNumerics);
        Assert.False(options.AddValidationResponses);
    }

    [Fact]
    public void AddSpecGuard_uses_default_options_when_not_configured()
    {
        var services = new ServiceCollection();

        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SpecGuardOptions>();

        Assert.False(options.RejectAdditionalProperties);
        Assert.False(options.AllowStringNumerics);
        Assert.True(options.AddValidationResponses);
    }

    private sealed class CustomValidator : IRequestValidator
    {
        public void Initialize(JsonDocument openApiSpec)
        {
        }

        public bool MatchesOperation(HttpRequest request) => false;

        public ValueTask<IReadOnlyList<ValidationErrorResult.ValidationError>> ValidateAsync(HttpContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ValidationErrorResult.ValidationError>>(Array.Empty<ValidationErrorResult.ValidationError>());
    }
}
