using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using VStorage.Api.Auth;
using VStorage.Api.Configuration;
using VStorage.Api.Endpoints;
using VStorage.Api.Http;
using VStorage.Api.OpenApi;
using VStorage.Application;
using VStorage.Infrastructure;
using VStorage.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddVStorageOptions(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddVStorageAuthentication();
builder.Services.AddShareSignIn();
builder.Services.AddVStorageAuthorization();
builder.Services.AddVStorageRateLimiting();
builder.Services.AddVStorageOpenApi();
builder.Services.AddVStorageProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();

// Development: on (creates vstoragedb on first start). Production: off, migrations are applied explicitly.
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await app.Services.ApplyMigrationsAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseMiddleware<PrincipalProvisioningMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

// OpenAPI document + Scalar UI with Keycloak sign-in (VStorage:Docs:Enabled).
app.MapVStorageDocs();

// Liveness = the process answers; readiness (database, blob root, disk space) is added in stage S10.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthTags.Live),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

// Browser download links /d/{token}: no prefix, no session (FR-D4, FR-A12).
app.MapDownloadEndpoints();

// Share links /s/{slug}: public, or Keycloak sign-in with a cookie on path /s (FR-S5, FR-A9..A11).
app.MapSharePages();

var api = app.MapGroup("/api/v1");
api.MapUserEndpoints();
api.MapBucketEndpoints();
api.MapAdminEndpoints();
api.MapObjectEndpoints();
api.MapDownloadLinkEndpoints();
api.MapShareLinkEndpoints();

await app.RunAsync();

/// <summary>Entry point; public for WebApplicationFactory in integration tests.</summary>
public partial class Program;
