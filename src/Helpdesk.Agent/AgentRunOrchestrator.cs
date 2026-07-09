using Azure.AI.Agents.Persistent;
using Helpdesk.Agent.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helpdesk.Agent;

/// <summary>
/// Executes a single agent run turn: posts the user message, polls the run to a terminal
/// state, dispatches tool calls, and returns the assistant's formatted reply.
/// Invokes the registered <see cref="IAgentMiddleware"/> pipeline before and after each turn.
/// </summary>
public sealed class AgentRunOrchestrator
{
    private readonly PersistentAgentsClient _client;
    private readonly HelpdeskApiClient _api;
    private readonly AgentOptions _options;
    private readonly IReadOnlyList<IAgentMiddleware> _middlewares;
    private readonly ILogger<AgentRunOrchestrator> _logger;

    public AgentRunOrchestrator(
        PersistentAgentsClient client,
        HelpdeskApiClient api,
        IOptions<AgentOptions> options,
        IEnumerable<IAgentMiddleware> middlewares,
        ILogger<AgentRunOrchestrator> logger)
    {
        _client = client;
        _api = api;
        _options = options.Value;
        _middlewares = middlewares.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Runs a single chat turn for the given context. Creates a linked timeout CancellationToken,
    /// invokes before/after middleware, and delegates to the inner run-loop.
    /// </summary>
    public async Task<string> RunTurnAsync(AgentRunContext context, CancellationToken outerCt = default)
    {
        var timeout = TimeSpan.FromSeconds(_options.RunTimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        timeoutCts.CancelAfter(timeout);

        foreach (var mw in _middlewares)
            await mw.BeforeRunAsync(context, outerCt);

        string reply;
        try
        {
            reply = await ExecuteRunAsync(context, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!outerCt.IsCancellationRequested)
        {
            _logger.LogWarning("Run for session {SessionId} timed out after {Timeout}s", context.SessionId, _options.RunTimeoutSeconds);
            reply = "Sorry, that request took too long to process. Please try again.";
        }

        foreach (var mw in _middlewares)
            await mw.AfterRunAsync(context, reply, outerCt);

        return reply;
    }

    // ── inner run-loop ────────────────────────────────────────────────────────

    private async Task<string> ExecuteRunAsync(AgentRunContext context, CancellationToken ct)
    {
        await _client.Messages.CreateMessageAsync(context.ThreadId, MessageRole.User, context.Message, cancellationToken: ct);

        var run = await _client.Runs.CreateRunAsync(context.ThreadId, context.AgentId, cancellationToken: ct);
        context.RunId = run.Value.Id;

        while (run.Value.Status == RunStatus.Queued
            || run.Value.Status == RunStatus.InProgress
            || run.Value.Status == RunStatus.RequiresAction)
        {
            if (run.Value.Status == RunStatus.RequiresAction
                && run.Value.RequiredAction is SubmitToolOutputsAction submitAction)
            {
                run = await DispatchToolCallsAsync(context, run.Value, submitAction, ct);
            }
            else
            {
                await Task.Delay(500, ct);
                run = await _client.Runs.GetRunAsync(context.ThreadId, run.Value.Id, ct);
            }
        }

        if (run.Value.Status != RunStatus.Completed)
        {
            _logger.LogWarning("Run {RunId} ended with status {Status}: {Error}",
                run.Value.Id, run.Value.Status, run.Value.LastError?.Message);
            return "Sorry, I couldn't process that request right now.";
        }

        await foreach (var msg in _client.Messages.GetMessagesAsync(
            context.ThreadId, order: ListSortOrder.Descending, cancellationToken: ct))
        {
            if (msg.Role == MessageRole.Agent)
                return FormatReplyWithCitations(msg, context.KnowledgeBaseFileNames);
        }

        return "(no response)";
    }

    private async Task<Azure.Response<ThreadRun>> DispatchToolCallsAsync(
        AgentRunContext context,
        ThreadRun run,
        SubmitToolOutputsAction submitAction,
        CancellationToken ct)
    {
        var outputs = new List<ToolOutput>();
        foreach (var toolCall in submitAction.ToolCalls)
        {
            if (toolCall is RequiredFunctionToolCall functionCall)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await ToolDefinitions.InvokeAsync(functionCall.Name, functionCall.Arguments, _api, ct);
                sw.Stop();
                context.ToolCalls.Add(new ToolCallRecord(functionCall.Name, functionCall.Arguments, result, sw.Elapsed));
                outputs.Add(new ToolOutput(functionCall.Id, result));
            }
        }
        return await _client.Runs.SubmitToolOutputsToRunAsync(run, outputs, ct);
    }

    private static string FormatReplyWithCitations(
        PersistentThreadMessage msg,
        IReadOnlyDictionary<string, string> fileNames)
    {
        var textContents = msg.ContentItems.OfType<MessageTextContent>().ToList();
        var text = string.Concat(textContents.Select(c => c.Text));

        var citedFiles = textContents
            .SelectMany(c => c.Annotations)
            .OfType<MessageTextFileCitationAnnotation>()
            .Select(a => fileNames.GetValueOrDefault(a.FileId, a.FileId))
            .Distinct()
            .ToList();

        return citedFiles.Count > 0
            ? $"{text}\n\nSources: {string.Join(", ", citedFiles)}"
            : text;
    }
}
