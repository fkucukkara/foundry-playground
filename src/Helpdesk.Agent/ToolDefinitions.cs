using Azure.AI.Agents.Persistent;
using System.Text.Json;

namespace Helpdesk.Agent;

/// <summary>
/// Function tool definitions exposed to the agent, wired to Helpdesk.Api operations.
/// Demonstrates Foundry "tools" — the agent decides when to call these based on the user's request.
/// </summary>
public static class ToolDefinitions
{
    public static FunctionToolDefinition GetLeaveBalance { get; } = new(
        name: "get_leave_balance",
        description: "Get the current vacation/sick leave balance for a given user id.",
        parameters: BinaryData.FromObjectAsJson(new
        {
            type = "object",
            properties = new
            {
                userId = new { type = "string", description = "The employee's user id, e.g. 'alice'." }
            },
            required = new[] { "userId" }
        }));

    public static FunctionToolDefinition GetTicketStatus { get; } = new(
        name: "get_ticket_status",
        description: "Get the status and details of an existing IT/HR support ticket by its ticket id.",
        parameters: BinaryData.FromObjectAsJson(new
        {
            type = "object",
            properties = new
            {
                ticketId = new { type = "string", description = "The ticket id to look up, e.g. '123'." }
            },
            required = new[] { "ticketId" }
        }));

    public static FunctionToolDefinition ListTickets { get; } = new(
        name: "list_tickets",
        description: "List all IT/HR support tickets for a given user id.",
        parameters: BinaryData.FromObjectAsJson(new
        {
            type = "object",
            properties = new
            {
                userId = new { type = "string", description = "The employee's user id, e.g. 'alice'." }
            },
            required = new[] { "userId" }
        }));

    public static FunctionToolDefinition CreateTicket { get; } = new(
        name: "create_ticket",
        description: "Open a new IT or HR support ticket on behalf of the user.",
        parameters: BinaryData.FromObjectAsJson(new
        {
            type = "object",
            properties = new
            {
                userId = new { type = "string", description = "The employee's user id, e.g. 'alice'." },
                category = new { type = "string", description = "Either 'IT' or 'HR'.", @enum = new[] { "IT", "HR" } },
                subject = new { type = "string", description = "A short subject line for the ticket." },
                description = new { type = "string", description = "A detailed description of the issue or request." }
            },
            required = new[] { "userId", "category", "subject", "description" }
        }));

    public static IReadOnlyList<FunctionToolDefinition> All { get; } =
        [GetLeaveBalance, GetTicketStatus, ListTickets, CreateTicket];

    /// <summary>Dispatches a tool call by name to the corresponding Helpdesk.Api operation.</summary>
    public static async Task<string> InvokeAsync(string toolName, string argumentsJson, HelpdeskApiClient api, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        return toolName switch
        {
            "get_leave_balance" => await api.GetLeaveBalanceAsync(root.GetProperty("userId").GetString()!, ct),
            "get_ticket_status" => await api.GetTicketStatusAsync(root.GetProperty("ticketId").GetString()!, ct),
            "list_tickets" => await api.ListTicketsAsync(root.GetProperty("userId").GetString()!, ct),
            "create_ticket" => await api.CreateTicketAsync(
                root.GetProperty("userId").GetString()!,
                root.GetProperty("category").GetString()!,
                root.GetProperty("subject").GetString()!,
                root.GetProperty("description").GetString()!,
                ct),
            _ => JsonSerializer.Serialize(new { error = $"Unknown tool '{toolName}'." })
        };
    }
}
