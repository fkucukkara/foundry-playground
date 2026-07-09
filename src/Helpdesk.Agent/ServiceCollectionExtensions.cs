using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Helpdesk.Agent.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helpdesk.Agent;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Helpdesk Copilot agent services (options, API client, orchestrator, middleware, agent service).</summary>
    public static IServiceCollection AddHelpdeskAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection("Agent"));

        // Shared Foundry SDK client — one instance for the lifetime of the app.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            return new PersistentAgentsClient(opts.ProjectEndpoint, new DefaultAzureCredential());
        });

        services.AddHttpClient<HelpdeskApiClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            client.BaseAddress = new Uri(opts.HelpdeskApiBaseUrl);
        });

        // Harness middleware pipeline — add more IAgentMiddleware impls here as needed.
        services.AddSingleton<IAgentMiddleware, TelemetryMiddleware>();

        services.AddSingleton<AgentRunOrchestrator>();
        services.AddSingleton<HelpdeskAgentService>();
        return services;
    }
}
