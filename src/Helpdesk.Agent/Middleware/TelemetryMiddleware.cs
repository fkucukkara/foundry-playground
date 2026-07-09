using Microsoft.Extensions.Logging;

namespace Helpdesk.Agent.Middleware;

/// <summary>
/// Harness middleware that emits structured telemetry for every agent run turn:
/// - Run start/end with elapsed time
/// - Each tool call name and duration
/// - Final run status
/// </summary>
public sealed class TelemetryMiddleware(ILogger<TelemetryMiddleware> logger) : IAgentMiddleware
{
    public Task BeforeRunAsync(AgentRunContext context, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Agent run starting — Session={SessionId} Thread={ThreadId} MessageLength={Length}",
            context.SessionId, context.ThreadId, context.Message.Length);

        return Task.CompletedTask;
    }

    public Task AfterRunAsync(AgentRunContext context, string reply, CancellationToken ct = default)
    {
        var elapsed = DateTimeOffset.UtcNow - context.StartedAt;

        foreach (var call in context.ToolCalls)
        {
            logger.LogInformation(
                "Tool call — Session={SessionId} Tool={Tool} Duration={DurationMs}ms ResultLength={ResultLength}",
                context.SessionId, call.ToolName, (int)call.Duration.TotalMilliseconds, call.Result.Length);
        }

        logger.LogInformation(
            "Agent run finished — Session={SessionId} RunId={RunId} ToolCalls={ToolCallCount} Elapsed={ElapsedMs}ms ReplyLength={ReplyLength}",
            context.SessionId, context.RunId ?? "n/a", context.ToolCalls.Count,
            (int)elapsed.TotalMilliseconds, reply.Length);

        return Task.CompletedTask;
    }
}
