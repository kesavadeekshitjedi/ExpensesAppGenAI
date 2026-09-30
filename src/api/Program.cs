using Azure.Monitor.OpenTelemetry.AspNetCore;
using Expenses.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// The connection string is absent in unit tests (WebApplicationFactory) and in any environment
// without a database configured, so registration is guarded like the telemetry block below.
var connectionString = builder.Configuration.GetConnectionString("Expenses");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<ExpensesDbContext>(options => options.UseSqlServer(connectionString));
}

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
