namespace Helpdesk.Agent;

/// <summary>
/// Carries all state for a single chat turn through the agent middleware pipeline.
/// Created by <see cref="HelpdeskAgentService"/> and passed into <see cref="AgentRunOrchestrator"/>.
/// </summary>
public class AgentRunContext
{
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public required string AgentId { get; init; }
    public required string Message { get; init; }

    /// <summary>Set by the orchestrator once the run is created.</summary>
    public string? RunId { get; set; }

    /// <summary>Populated by the orchestrator as each tool call completes.</summary>
    public List<ToolCallRecord> ToolCalls { get; } = [];

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Maps file IDs returned by File Search to human-readable file names for citation formatting.</summary>
    public IReadOnlyDictionary<string, string> KnowledgeBaseFileNames { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>An individual tool call made during a run turn, with timing for telemetry.</summary>
public record ToolCallRecord(string ToolName, string Arguments, string Result, TimeSpan Duration);
