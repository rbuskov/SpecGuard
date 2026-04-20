using Scalar.AspNetCore;
using SpecGuard.TicTacToeApi.Endpoints;
using SpecGuard.TicTacToeApi.OpenApi;
using SpecGuard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer<CoordinateRangeTransformer>();
    options.AddSchemaTransformer<BoardShapeTransformer>();
});
builder.Services.AddSpecGuard();

var app = builder.Build();

app.UseSpecGuard(); // Always register middleware before mapping endpoints
app.MapBoardEndpoints();
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();

public partial class Program;
