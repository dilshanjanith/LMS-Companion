using LmsCompanion.Infrastructure;
using LmsCompanion.Infrastructure.Security;
using LmsCompanion.Application;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddLmsBearerAuthentication(builder.Configuration);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = _ =>
        new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { error = "invalid_request" });
});
builder.Services.AddCors(options => options.AddPolicy("AngularApp", policy =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowedOrigins.Length == 0)
    {
        policy.SetIsOriginAllowed(_ => false);
        return;
    }

    policy.WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST", "DELETE", "OPTIONS")
        .WithHeaders("Authorization", "Content-Type");
}));
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseAuthentication();
app.UseCors("AngularApp");
app.UseAuthorization();
app.MapHealthChecks("/health", new HealthCheckOptions()).AllowAnonymous();
app.MapControllers();
app.MapGet("/", () => Results.Ok(new { service = "LmsCompanion.Api", status = "ready" })).RequireAuthorization();
app.Run();

public partial class Program;
