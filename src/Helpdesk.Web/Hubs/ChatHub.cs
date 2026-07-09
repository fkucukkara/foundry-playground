using Helpdesk.Agent;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace Helpdesk.Web.Hubs;

/// <summary>
/// SignalR hub exposing the Helpdesk Copilot agent over a persistent WebSocket connection.
/// Client calls <c>SendMessage</c>; the hub pushes <c>AgentTyping</c> and <c>ReceiveMessage</c> events.
/// This hub is available at <c>/chathub</c> for any SignalR-capable client
/// (Blazor components, mobile apps, desktop tools, etc.).
/// </summary>
[EnableRateLimiting("chat")]
public sealed class ChatHub(HelpdeskAgentService agent, ILogger<ChatHub> logger) : Hub
{
    public async Task SendMessage(string sessionId, string message)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(message)
            || message.Length > 4000)
        {
            await Clients.Caller.SendAsync("ReceiveError", "Invalid message.");
            return;
        }

        logger.LogDebug("ChatHub.SendMessage — ConnectionId={ConnectionId} Session={SessionId}", Context.ConnectionId, sessionId);

        await Clients.Caller.SendAsync("AgentTyping", true);
        try
        {
            var reply = await agent.ChatAsync(sessionId, message, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("ReceiveMessage", reply);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected mid-run — nothing to send back.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ChatHub: unhandled error for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", "Something went wrong. Please try again.");
        }
        finally
        {
            await Clients.Caller.SendAsync("AgentTyping", false);
        }
    }
}
