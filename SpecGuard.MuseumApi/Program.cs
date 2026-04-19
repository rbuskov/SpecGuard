using Scalar.AspNetCore;
using SpecGuard.MuseumApi.Endpoints;
using SpecGuard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSpecGuard();

var app = builder.Build();

app.UseSpecGuard(); // Always register middleware before mapping endpoints
app.MapMuseumHoursEndpoints();
app.MapSpecialEventEndpoints();
app.MapTicketEndpoints();
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();
