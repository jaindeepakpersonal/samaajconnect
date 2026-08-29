using Microsoft.EntityFrameworkCore;
using Sangam.Events.Api.Endpoints;
using Sangam.Events.Api.Extensions;
using Sangam.Events.Application;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Infrastructure;
using Sangam.Events.Infrastructure.Persistence;

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
    swagger.SwaggerDoc("v1", new() { Title = "Sangam Member & Family Service", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous()
    .WithTags("Diagnostics")
    .ExcludeFromDescription();

app.MapEventEndpoints();

await app.RunAsync();

/// <summary>
/// Exposed so the integration tests can host this exact composition root with
/// WebApplicationFactory rather than re-registering a parallel one that drifts.
/// </summary>
public partial class Program;
