namespace Helpdesk.Agent;

/// <summary>Configuration for the Helpdesk Copilot agent, bound from configuration/env vars.</summary>
public class AgentOptions
{
    /// <summary>Foundry project endpoint, e.g. https://&lt;account&gt;.services.ai.azure.com/api/projects/&lt;project&gt;</summary>
    public string ProjectEndpoint { get; set; } = string.Empty;

    /// <summary>Name of the deployed chat-completion model to use, e.g. gpt-4o-mini.</summary>
    public string ModelDeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the Helpdesk.Api service used by the function tool. Defaults to the Aspire
    /// service-discovery logical name ("api"), resolved via Microsoft.Extensions.ServiceDiscovery
    /// to the actual endpoint (dynamic local port when run under the AppHost, internal Container
    /// Apps DNS name in Azure). Override via configuration for scenarios that bypass the AppHost.
    /// </summary>
    public string HelpdeskApiBaseUrl { get; set; } = "http://api";

    /// <summary>
    /// Folder containing the knowledge base markdown files to index for File Search. Resolved relative
    /// to the app's base directory (AppContext.BaseDirectory) when not rooted - this matches where the
    /// "docs/knowledge-base" content is copied to at build/publish time (see Helpdesk.Web.csproj), so it
    /// works identically under `dotnet run`, the Aspire AppHost, and a published/containerized app.
    /// </summary>
    public string KnowledgeBaseDirectory { get; set; } = "docs/knowledge-base";

    public const string AgentName = "helpdesk-copilot";

    /// <summary>Maximum seconds to wait for a single agent run to reach a terminal state. Default: 90.</summary>
    public int RunTimeoutSeconds { get; set; } = 90;
}
