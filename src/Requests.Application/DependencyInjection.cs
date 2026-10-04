using Microsoft.Extensions.DependencyInjection;
using Requests.Application.Requests.Services;

namespace Requests.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRequestQueryService, RequestQueryService>();
        services.AddScoped<IRequestCommandService, RequestCommandService>();
        services.AddScoped<IRequestSummaryService, RequestSummaryService>();
        return services;
    }
}
