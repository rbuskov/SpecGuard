using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.Test.Sanitizers;

public class EmailAddressSchemaTransformerTests
{
    private sealed class HasEmail
    {
        [EmailAddress]
        public string? Address { get; set; }

        public string? Plain { get; set; }
    }

    [Fact]
    public async Task Sets_email_format_for_string_property_decorated_with_EmailAddress()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        var context = CreateContext(typeof(HasEmail), nameof(HasEmail.Address));

        await new EmailAddressSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Equal("email", schema.Format);
    }

    [Fact]
    public async Task Does_not_set_format_for_string_without_attribute()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        var context = CreateContext(typeof(HasEmail), nameof(HasEmail.Plain));

        await new EmailAddressSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
    }

    [Fact]
    public async Task Does_not_set_format_when_no_property_info_is_available()
    {
        // Top-level schema (JsonPropertyInfo is null) — no attribute to inspect.
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        var context = CreateContext(typeof(string), propertyName: null);

        await new EmailAddressSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
    }

    [Fact]
    public async Task Does_not_set_format_for_non_string_type_even_with_attribute()
    {
        // The transformer gates on JsonTypeInfo.Type == typeof(string); the
        // attribute alone is not enough. Integer values should never receive
        // format: email even if misannotated upstream.
        var schema = new OpenApiSchema { Type = JsonSchemaType.Integer };
        var context = CreateContext(typeof(HasMisannotated), nameof(HasMisannotated.Count));

        await new EmailAddressSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
    }

    private sealed class HasMisannotated
    {
        [EmailAddress]
        public int Count { get; set; }
    }

    private static OpenApiSchemaTransformerContext CreateContext(Type declaringType, string? propertyName)
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var typeInfo = options.GetTypeInfo(declaringType);

        JsonPropertyInfo? propertyInfo = null;
        Type schemaType = declaringType;
        if (propertyName is not null)
        {
            propertyInfo = typeInfo.Properties.First(p => p.Name == NamingPolicyCamelCase(propertyName));
            schemaType = propertyInfo.PropertyType;
        }

        return new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = options.GetTypeInfo(schemaType),
            JsonPropertyInfo = propertyInfo,
            ParameterDescription = null,
            ApplicationServices = null!,
        };
    }

    private static string NamingPolicyCamelCase(string name) =>
        // Default resolver uses source-declared names (PascalCase); the JSON
        // property name matches the CLR name here because no naming policy is
        // configured on the serializer options used in these tests.
        name;
}
