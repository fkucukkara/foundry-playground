using System.Collections.Concurrent;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helpdesk.Agent;

/// <summary>
/// Owns the lifecycle of the "Helpdesk Copilot" Foundry agent:
///   - Model: references the deployed chat model via <see cref="AgentOptions.ModelDeploymentName"/>.
///   - Agent: created once and reused (idempotent ensure-on-startup).
///   - Knowledge base: markdown docs uploaded to a File Search vector store for grounded Q&A.
///   - Tools: a function tool wired to Helpdesk.Api for real actions (tickets, leave balance).
/// </summary>
public class HelpdeskAgentService
{
    private const string Instructions = """
        You are Helpdesk Copilot, an internal IT/HR assistant.
        - For policy questions, use the file search tool to ground your answer in the knowledge base and mention which document you used.
        - For anything about a ticket or leave balance, use the available function tools instead of guessing — never make up ticket ids or balances.
        - If you need a userId to call a tool and don't have one, ask the user for it first.
        - Keep answers short and to the point.
        """;

    /// <summary>Maximum time to wait for a single agent run to finish before giving up and returning to the caller.</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(90);

    private readonly PersistentAgentsClient _client;
    private readonly HelpdeskApiClient _api;
    private readonly AgentOptions _options;
    private readonly ILogger<HelpdeskAgentService> _logger;
    private readonly ConcurrentDictionary<string, string> _sessionThreads = new();
    private readonly ConcurrentDictionary<string, string> _knowledgeBaseFileNames = new();

    private string? _agentId;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public HelpdeskAgentService(IOptions<AgentOptions> options, HelpdeskApiClient api, ILogger<HelpdeskAgentService> logger)
    {
        _options = options.Value;
        _api = api;
        _logger = logger;
        _client = new PersistentAgentsClient(_options.ProjectEndpoint, new DefaultAzureCredential());
    }

    /// <summary>Ensures the agent (and its knowledge base) exist. Safe to call repeatedly — reuses an existing agent by name.</summary>
    public async Task<string> EnsureAgentAsync(CancellationToken ct = default)
    {
        if (_agentId is not null)
        {
            return _agentId;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_agentId is not null)
            {
                return _agentId;
            }

            await foreach (var existing in _client.Administration.GetAgentsAsync(cancellationToken: ct))
            {
                if (existing.Name == AgentOptions.AgentName)
                {
                    _agentId = existing.Id;
                    _logger.LogInformation("Reusing existing agent {AgentId}", _agentId);
                    return _agentId;
                }
            }

            var vectorStoreId = await CreateKnowledgeBaseVectorStoreAsync(ct);

            var tools = new List<ToolDefinition>(ToolDefinitions.All) { new FileSearchToolDefinition() };
            var toolResources = new ToolResources
            {
                FileSearch = new FileSearchToolResource()
            };
            toolResources.FileSearch.VectorStoreIds.Add(vectorStoreId);

            var agent = await _client.Administration.CreateAgentAsync(
                model: _options.ModelDeploymentName,
                name: AgentOptions.AgentName,
                instructions: Instructions,
                tools: tools,
                toolResources: toolResources,
                cancellationToken: ct);

            _agentId = agent.Value.Id;
            _logger.LogInformation("Created new agent {AgentId}", _agentId);
            return _agentId;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<string> CreateKnowledgeBaseVectorStoreAsync(CancellationToken ct)
    {
        var directory = Path.IsPathRooted(_options.KnowledgeBaseDirectory)
            ? _options.KnowledgeBaseDirectory
            : Path.Combine(AppContext.BaseDirectory, _options.KnowledgeBaseDirectory);

        var fileIds = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.md"))
        {
            await using var stream = File.OpenRead(path);
            var fileName = Path.GetFileName(path);
            var uploaded = await _client.Files.UploadFileAsync(stream, PersistentAgentFilePurpose.Agents, fileName, cancellationToken: ct);
            fileIds.Add(uploaded.Value.Id);
            _knowledgeBaseFileNames[uploaded.Value.Id] = fileName;
        }

        var vectorStore = await _client.VectorStores.CreateVectorStoreAsync(fileIds: fileIds, name: "helpdesk-knowledge-base", cancellationToken: ct);
        _logger.LogInformation("Created knowledge base vector store {VectorStoreId} with {Count} documents", vectorStore.Value.Id, fileIds.Count);
        return vectorStore.Value.Id;
    }

    /// <summary>Sends a user message on the given session's thread (creating the thread if new) and returns the assistant's reply.</summary>
    public async Task<string> ChatAsync(string sessionId, string message, CancellationToken ct = default)
    {
        var agentId = await EnsureAgentAsync(ct);
        var threadId = await GetOrCreateThreadAsync(sessionId, ct);

        // Guard against a run that never reaches a terminal status (e.g. a stuck backend) hanging the request forever.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RunTimeout);

        try
        {
            await _client.Messages.CreateMessageAsync(threadId, MessageRole.User, message, cancellationToken: timeoutCts.Token);

            var run = await _client.Runs.CreateRunAsync(threadId, agentId, cancellationToken: timeoutCts.Token);

            while (run.Value.Status == RunStatus.Queued || run.Value.Status == RunStatus.InProgress || run.Value.Status == RunStatus.RequiresAction)
            {
                if (run.Value.Status == RunStatus.RequiresAction && run.Value.RequiredAction is SubmitToolOutputsAction submitAction)
                {
                    var outputs = new List<ToolOutput>();
                    foreach (var toolCall in submitAction.ToolCalls)
                    {
                        if (toolCall is RequiredFunctionToolCall functionCall)
                        {
                            var result = await ToolDefinitions.InvokeAsync(functionCall.Name, functionCall.Arguments, _api, timeoutCts.Token);
                            outputs.Add(new ToolOutput(functionCall.Id, result));
                        }
                    }
                    run = await _client.Runs.SubmitToolOutputsToRunAsync(run.Value, outputs, timeoutCts.Token);
                }
                else
                {
                    await Task.Delay(500, timeoutCts.Token);
                    run = await _client.Runs.GetRunAsync(threadId, run.Value.Id, timeoutCts.Token);
                }
            }

            if (run.Value.Status != RunStatus.Completed)
            {
                _logger.LogWarning("Run ended with status {Status}: {Error}", run.Value.Status, run.Value.LastError?.Message);
                return "Sorry, I couldn't process that request right now.";
            }

            await foreach (var msg in _client.Messages.GetMessagesAsync(threadId, order: ListSortOrder.Descending, cancellationToken: timeoutCts.Token))
            {
                if (msg.Role == MessageRole.Agent)
                {
                    return FormatReplyWithCitations(msg);
                }
            }

            return "(no response)";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Run for session {SessionId} timed out after {Timeout}", sessionId, RunTimeout);
            return "Sorry, that request took too long to process. Please try again.";
        }
    }

    /// <summary>Builds the assistant's reply text and appends the knowledge-base source documents cited by the File Search tool, if any.</summary>
    private string FormatReplyWithCitations(PersistentThreadMessage msg)
    {
        var textContents = msg.ContentItems.OfType<MessageTextContent>().ToList();
        var text = string.Concat(textContents.Select(c => c.Text));

        var citedFiles = textContents
            .SelectMany(c => c.Annotations)
            .OfType<MessageTextFileCitationAnnotation>()
            .Select(a => _knowledgeBaseFileNames.GetValueOrDefault(a.FileId, a.FileId))
            .Distinct()
            .ToList();

        return citedFiles.Count > 0
            ? $"{text}\n\nSources: {string.Join(", ", citedFiles)}"
            : text;
    }

    private async Task<string> GetOrCreateThreadAsync(string sessionId, CancellationToken ct)
    {
        if (_sessionThreads.TryGetValue(sessionId, out var threadId))
        {
            return threadId;
        }

        var thread = await _client.Threads.CreateThreadAsync(cancellationToken: ct);
        _sessionThreads[sessionId] = thread.Value.Id;
        return thread.Value.Id;
    }
}
