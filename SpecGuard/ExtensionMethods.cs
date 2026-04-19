using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpecGuard.Sanitizers;
using SpecGuard.Validators;

namespace SpecGuard;

public static class ExtensionMethods
{
    private const string DefaultSpecUrl = "/openapi/v1.json";

    public static IServiceCollection AddSpecGuard(this IServiceCollection services)
        => services.AddSpecGuard(_ => { });

    public static IServiceCollection AddSpecGuard(this IServiceCollection services, Action<SpecGuardOptions> configure)
    {
        var options = new SpecGuardOptions();
        configure(options);

        services.AddSingleton(options);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestValidator, ParameterValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestValidator, JsonBodyValidator>());
        services.ConfigureAll<OpenApiOptions>(o =>
        {
            o.AddSchemaTransformer<NumericSchemaTransformer>();
            o.AddSchemaTransformer<SbyteSchemaTransformer>();
            o.AddSchemaTransformer<HalfSchemaTransformer>();
            o.AddSchemaTransformer<TimeSpanSchemaTransformer>();
            o.AddSchemaTransformer<EmailAddressSchemaTransformer>();

            if (options.AddValidationResponses)
            {
                o.AddOperationTransformer<ValidationResponseTransformer>();
            }
        });

        services.PostConfigureAll<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy ??= JsonNamingPolicy.CamelCase;

            if (!o.SerializerOptions.Converters.OfType<JsonStringEnumConverter>().Any())
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        return services;
    }

    public static IApplicationBuilder UseSpecGuard(this IApplicationBuilder app, string specUrl = DefaultSpecUrl)
    {
        return app.UseMiddleware<SpecGuardMiddleware>(specUrl);
    }
}