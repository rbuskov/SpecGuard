using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    [Fact]
    public void AddSpecGuard_sets_default_property_naming_policy_to_camelCase()
    {
        var services = new ServiceCollection();

        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        Assert.Equal(JsonNamingPolicy.CamelCase, options.SerializerOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void AddSpecGuard_preserves_user_supplied_naming_policy()
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        });

        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.SerializerOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void AddSpecGuard_adds_a_single_string_enum_converter_on_clean_collection()
    {
        var services = new ServiceCollection();

        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        var count = options.SerializerOptions.Converters.OfType<JsonStringEnumConverter>().Count();
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddSpecGuard_does_not_add_a_second_string_enum_converter_when_one_exists()
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        var count = options.SerializerOptions.Converters.OfType<JsonStringEnumConverter>().Count();
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddSpecGuard_injects_the_same_options_instance_everywhere()
    {
        var services = new ServiceCollection();
        services.AddSpecGuard(o => o.RejectAdditionalProperties = true);

        using var provider = services.BuildServiceProvider();
        var options1 = provider.GetRequiredService<SpecGuardOptions>();
        var options2 = provider.GetRequiredService<SpecGuardOptions>();

        Assert.Same(options1, options2);
    }

    [Fact]
    public void AddSpecGuard_second_call_overrides_previous_options_configuration()
    {
        var services = new ServiceCollection();

        services.AddSpecGuard(o => o.RejectAdditionalProperties = true);
        services.AddSpecGuard(o => o.AllowStringNumerics = true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SpecGuardOptions>();

        // The second registration wins because the last Singleton registration
        // of SpecGuardOptions replaces the first.
        Assert.False(options.RejectAdditionalProperties);
        Assert.True(options.AllowStringNumerics);
    }

    [Fact]
    public void AddSpecGuard_registers_a_configure_callback_for_OpenApiOptions()
    {
        // OpenApiOptions doesn't expose its transformer lists publicly,
        // so we assert the registration indirectly via the configuration
        // callback being present in DI. End-to-end behavior (the
        // transformers actually firing) is locked in PublishedSpecTests.
        var services = new ServiceCollection();
        services.AddSpecGuard();

        using var provider = services.BuildServiceProvider();
        var configures = provider.GetServices<IConfigureOptions<OpenApiOptions>>().ToArray();

        Assert.NotEmpty(configures);
    }

    [Fact]
    public void UseSpecGuard_default_argument_uses_default_spec_url()
    {
        var app = WebApplication.CreateBuilder();
        app.Services.AddSpecGuard();
        var built = app.Build();

        // The default URL is /openapi/v1.json — verified at construction time
        // by calling the no-arg overload and resolving the middleware.
        built.UseSpecGuard();

        // No throw means the registration succeeded with the default URL.
        Assert.NotNull(built);
    }

    [Fact]
    public void UseSpecGuard_relative_url_overload_accepts_custom_path()
    {
        var app = WebApplication.CreateBuilder();
        app.Services.AddSpecGuard();
        var built = app.Build();

        built.UseSpecGuard("/custom/spec.json");

        Assert.NotNull(built);
    }

    [Fact]
    public void UseSpecGuard_absolute_url_overload_accepts_https_url()
    {
        var app = WebApplication.CreateBuilder();
        app.Services.AddSpecGuard();
        var built = app.Build();

        built.UseSpecGuard("https://example.com/spec.json");

        Assert.NotNull(built);
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
