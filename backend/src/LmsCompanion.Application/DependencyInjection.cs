using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LmsCompanion.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuthenticatedUserContextResolver, AuthenticatedUserContextResolver>();
        services.AddScoped<ICanvasOAuthFlow, CanvasOAuthFlow>();
        services.AddScoped<ICanvasAccessTokenService, CanvasAccessTokenService>();
        services.AddScoped<IConnectionManagementService, ConnectionManagementService>();
        services.AddScoped<ILmsQueryService, LmsQueryService>();
        return services;
    }
}
