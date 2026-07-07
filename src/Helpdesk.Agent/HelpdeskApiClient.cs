using System.Net.Http.Json;
using System.Text.Json;

namespace Helpdesk.Agent;

/// <summary>
/// Thin client wrapping the Helpdesk.Api REST endpoints. This is the backend the agent's
/// function tool calls into to take real actions (as opposed to just answering from knowledge).
/// </summary>
public class HelpdeskApiClient(HttpClient httpClient)
{
    public async Task<string> GetLeaveBalanceAsync(string userId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"/api/leave-balance/{Uri.EscapeDataString(userId)}", ct);
        if (!response.IsSuccessStatusCode)
        {
            return JsonSerializer.Serialize(new { error = $"No leave balance found for user '{userId}'." });
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<string> GetTicketStatusAsync(string ticketId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"/api/tickets/{Uri.EscapeDataString(ticketId)}", ct);
        if (!response.IsSuccessStatusCode)
        {
            return JsonSerializer.Serialize(new { error = $"No ticket found with id '{ticketId}'." });
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<string> ListTicketsAsync(string userId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"/api/tickets?userId={Uri.EscapeDataString(userId)}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<string> CreateTicketAsync(string userId, string category, string subject, string description, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/tickets", new
        {
            userId,
            category,
            subject,
            description
        }, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
