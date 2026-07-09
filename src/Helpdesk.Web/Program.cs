using Helpdesk.Agent;
using Helpdesk.Web.Hubs;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHelpdeskAgent(builder.Configuration);

// Blazor Server (interactive server-side rendering)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SignalR — for the ChatHub available at /chathub
builder.Services.AddSignalR();

// Rate limiter: 10 chat requests/min per IP — applied to ChatHub.SendMessage
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

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseAntiforgery();
app.UseRateLimiter();

// Blazor Server — serves the root App.razor component
app.MapRazorComponents<Helpdesk.Web.App>()
    .AddInteractiveServerRenderMode();

// SignalR hub — real-time chat endpoint for any SignalR-capable client
app.MapHub<ChatHub>("/chathub");

app.Run();


