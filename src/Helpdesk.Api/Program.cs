using Helpdesk.Api.Models;
using Helpdesk.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddSingleton<HelpdeskStore>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", () => Results.Ok("Healthy"));

app.MapGet("/api/tickets", (string userId, HelpdeskStore store) =>
    Results.Ok(store.GetTicketsForUser(userId)))
    .WithName("ListTickets");

app.MapGet("/api/tickets/{ticketId}", (string ticketId, HelpdeskStore store) =>
{
    var ticket = store.GetTicket(ticketId);
    return ticket is null ? Results.NotFound() : Results.Ok(ticket);
})
    .WithName("GetTicketStatus");

app.MapPost("/api/tickets", (CreateTicketRequest request, HelpdeskStore store) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.BadRequest(new { errors });
    }

    var ticket = store.CreateTicket(request);
    return Results.Created($"/api/tickets/{ticket.TicketId}", ticket);
})
    .WithName("CreateTicket");

app.MapGet("/api/leave-balance/{userId}", (string userId, HelpdeskStore store) =>
{
    var balance = store.GetLeaveBalance(userId);
    return balance is null ? Results.NotFound() : Results.Ok(balance);
})
    .WithName("GetLeaveBalance");

app.Run();
