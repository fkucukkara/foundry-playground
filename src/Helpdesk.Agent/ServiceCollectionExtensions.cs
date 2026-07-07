using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.Agent;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Helpdesk Copilot agent services (options, API client, agent service).</summary>
    public static IServiceCollection AddHelpdeskAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection("Agent"));

        services.AddHttpClient<HelpdeskApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
            client.BaseAddress = new Uri(options.HelpdeskApiBaseUrl);
        });

        services.AddSingleton<HelpdeskAgentService>();
        return services;
    }
}
