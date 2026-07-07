using Helpdesk.Agent;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHelpdeskAgent(builder.Configuration);

// Cheap abuse/cost control: cap chat requests per client so a single user (or bot) can't
// hammer the model deployment. Partitioned by remote IP since there is no end-user auth (see PRD Non-Goals).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok("Healthy"));

app.MapPost("/api/chat", async (ChatRequest request, HelpdeskAgentService agent, CancellationToken ct) =>
{
    if (!request.IsValid)
    {
        return Results.BadRequest(new { error = $"sessionId and message are required; message must be {ChatRequest.MaxMessageLength} characters or fewer." });
    }

    var reply = await agent.ChatAsync(request.SessionId, request.Message, ct);
    return Results.Ok(new ChatResponse(reply));
}).RequireRateLimiting("chat");

app.Run();

record ChatRequest(string SessionId, string Message)
{
    public const int MaxMessageLength = 4000;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(SessionId)
        && !string.IsNullOrWhiteSpace(Message)
        && Message.Length <= MaxMessageLength;
}

record ChatResponse(string Reply);

