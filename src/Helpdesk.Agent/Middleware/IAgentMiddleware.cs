namespace Helpdesk.Agent.Middleware;

/// <summary>
/// Harness middleware hook invoked before and after each agent run turn.
/// Implement this interface for cross-cutting concerns (telemetry, logging, guardrails, etc.)
/// and register all implementations with DI — the orchestrator runs them all in order.
/// </summary>
public interface IAgentMiddleware
{
    /// <summary>Called immediately before the agent run is created. Use to enrich context or block bad inputs.</summary>
    Task BeforeRunAsync(AgentRunContext context, CancellationToken ct = default);

    /// <summary>Called after the run reaches a terminal state. Use to log, emit metrics, or post-process the reply.</summary>
    Task AfterRunAsync(AgentRunContext context, string reply, CancellationToken ct = default);
}
