using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SpecGuard.Sanitizers;

namespace SpecGuard.Test.Sanitizers;

public class ValidationResponseTransformerTests
{
    [Fact]
    public async Task No_params_and_no_body_adds_nothing()
    {
        var operation = new OpenApiOperation();

        await Transform(operation);

        Assert.True(operation.Responses is null || operation.Responses.Count == 0);
    }

    [Fact]
    public async Task Parameters_only_adds_422_not_400()
    {
        var operation = new OpenApiOperation
        {
            Parameters = new List<IOpenApiParameter>
            {
                new OpenApiParameter { Name = "id", In = ParameterLocation.Path },
            },
        };

        await Transform(operation);

        Assert.NotNull(operation.Responses);
        Assert.False(operation.Responses!.ContainsKey("400"));
        Assert.True(operation.Responses.ContainsKey("422"));
    }

    [Fact]
    public async Task Json_request_body_adds_400_and_422()
    {
        var operation = OperationWithBody("application/json");

        await Transform(operation);

        Assert.True(operation.Responses!.ContainsKey("400"));
        Assert.True(operation.Responses.ContainsKey("422"));
    }

    [Fact]
    public async Task Vendor_plus_json_body_is_treated_as_json()
    {
        var operation = OperationWithBody("application/vnd.myapp.v1+json");

        await Transform(operation);

        Assert.True(operation.Responses!.ContainsKey("400"));
        Assert.True(operation.Responses.ContainsKey("422"));
    }

    [Fact]
    public async Task Json_with_charset_parameter_is_treated_as_json()
    {
        var operation = OperationWithBody("application/json; charset=utf-8");

        await Transform(operation);

        Assert.True(operation.Responses!.ContainsKey("400"));
        Assert.True(operation.Responses.ContainsKey("422"));
    }

    [Fact]
    public async Task Non_json_body_without_params_adds_nothing()
    {
        var operation = OperationWithBody("multipart/form-data");

        await Transform(operation);

        Assert.True(operation.Responses is null || operation.Responses.Count == 0);
    }

    [Fact]
    public async Task Non_json_body_with_params_adds_422_only()
    {
        var operation = OperationWithBody("multipart/form-data");
        operation.Parameters = new List<IOpenApiParameter>
        {
            new OpenApiParameter { Name = "id", In = ParameterLocation.Path },
        };

        await Transform(operation);

        Assert.False(operation.Responses!.ContainsKey("400"));
        Assert.True(operation.Responses.ContainsKey("422"));
    }

    [Fact]
    public async Task Existing_400_is_not_overwritten()
    {
        var existing = new OpenApiResponse { Description = "user-defined" };
        var operation = OperationWithBody("application/json");
        operation.Responses = new OpenApiResponses { ["400"] = existing };

        await Transform(operation);

        Assert.Same(existing, operation.Responses["400"]);
        Assert.True(operation.Responses.ContainsKey("422"));
    }

    [Fact]
    public async Task Existing_422_is_not_overwritten()
    {
        var existing = new OpenApiResponse { Description = "user-defined" };
        var operation = OperationWithBody("application/json");
        operation.Responses = new OpenApiResponses { ["422"] = existing };

        await Transform(operation);

        Assert.Same(existing, operation.Responses["422"]);
        Assert.True(operation.Responses.ContainsKey("400"));
    }

    [Fact]
    public async Task Both_already_defined_is_no_op()
    {
        var existing400 = new OpenApiResponse { Description = "user-400" };
        var existing422 = new OpenApiResponse { Description = "user-422" };
        var operation = OperationWithBody("application/json");
        operation.Responses = new OpenApiResponses
        {
            ["400"] = existing400,
            ["422"] = existing422,
        };

        await Transform(operation);

        Assert.Same(existing400, operation.Responses["400"]);
        Assert.Same(existing422, operation.Responses["422"]);
        Assert.Equal(2, operation.Responses.Count);
    }

    [Fact]
    public async Task Added_422_declares_problem_json_with_errors_array()
    {
        var operation = OperationWithBody("application/json");

        await Transform(operation);

        var response = operation.Responses!["422"];
        var media = Assert.Contains("application/problem+json", (IDictionary<string, OpenApiMediaType>)response.Content!);
        var schema = Assert.IsType<OpenApiSchema>(media.Schema);
        Assert.Equal(JsonSchemaType.Object, schema.Type);
        Assert.Contains("errors", schema.Properties!.Keys);
        var errors = Assert.IsType<OpenApiSchema>(schema.Properties["errors"]);
        Assert.Equal(JsonSchemaType.Array, errors.Type);
        var item = Assert.IsType<OpenApiSchema>(errors.Items);
        Assert.Equal(JsonSchemaType.Object, item.Type);
        Assert.Contains("message", item.Properties!.Keys);
        Assert.Contains("in", item.Properties.Keys);
        Assert.Contains("path", item.Properties.Keys);
    }

    [Fact]
    public async Task Wildcard_4XX_does_not_prevent_explicit_400_and_422()
    {
        var existing = new OpenApiResponse { Description = "any client error" };
        var operation = OperationWithBody("application/json");
        operation.Responses = new OpenApiResponses { ["4XX"] = existing };

        await Transform(operation);

        Assert.Same(existing, operation.Responses["4XX"]);
        Assert.True(operation.Responses.ContainsKey("400"));
        Assert.True(operation.Responses.ContainsKey("422"));
    }

    [Fact]
    public async Task Added_400_declares_problem_json_without_errors_array()
    {
        var operation = OperationWithBody("application/json");

        await Transform(operation);

        var response = operation.Responses!["400"];
        var media = Assert.Contains("application/problem+json", (IDictionary<string, OpenApiMediaType>)response.Content!);
        var schema = Assert.IsType<OpenApiSchema>(media.Schema);
        Assert.Equal(JsonSchemaType.Object, schema.Type);
        Assert.DoesNotContain("errors", schema.Properties!.Keys);
    }

    private static Task Transform(OpenApiOperation operation) =>
        new ValidationResponseTransformer().TransformAsync(
            operation,
            new OpenApiOperationTransformerContext
            {
                DocumentName = "v1",
                Description = null!,
                ApplicationServices = null!,
            },
            CancellationToken.None);

    private static OpenApiOperation OperationWithBody(string mediaType) => new()
    {
        RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [mediaType] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.Object } },
            },
        },
    };
}
