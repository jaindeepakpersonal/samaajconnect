using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Api.Endpoints;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Infrastructure;
using Sangam.IdentityTenant.Infrastructure.Persistence;
using Sangam.IdentityTenant.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddJwtAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swagger =>
{
    swagger.SwaggerDoc("v1", new() { Title = "Sangam Identity & Tenant Service", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();
    await dbContext.Database.MigrateAsync();

    // Runs after migrations so the seeded roles it references already exist.
    await scope.ServiceProvider.GetRequiredService<SuperAdminBootstrapper>()
        .EnsureSuperAdminAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous()
    .WithTags("Diagnostics")
    .ExcludeFromDescription();

app.MapTenantEndpoints();
app.MapTenantLogoEndpoints();
app.MapAuthEndpoints();
app.MapActivationEndpoints();
app.MapAdminEndpoints();
app.MapConsentEndpoints();

await app.RunAsync();

/// <summary>
/// Exposed so the integration tests can host this exact composition root with
/// WebApplicationFactory rather than re-registering a parallel one that drifts.
/// </summary>
public partial class Program;
