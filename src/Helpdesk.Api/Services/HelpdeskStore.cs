using System.Collections.Concurrent;
using Helpdesk.Api.Models;

namespace Helpdesk.Api.Services;

/// <summary>Mock in-memory data store standing in for a real ticketing/HR system.</summary>
public class HelpdeskStore
{
    private readonly ConcurrentDictionary<string, Ticket> _tickets = new();
    private readonly ConcurrentDictionary<string, LeaveBalance> _leaveBalances = new();

    // Start comfortably above any seeded ticket id (e.g. "123" below) so generated ids can never collide with seed data.
    private int _nextTicketId = 1000;

    public HelpdeskStore()
    {
        // Seed demo users.
        _leaveBalances["alice"] = new LeaveBalance("alice", VacationDays: 12, SickDays: 7);
        _leaveBalances["bob"] = new LeaveBalance("bob", VacationDays: 18, SickDays: 10);

        var seedTicket = new Ticket
        {
            TicketId = "123",
            UserId = "alice",
            Category = "IT",
            Subject = "VPN not connecting",
            Description = "VPN client fails to connect from home network.",
            Status = "In Progress"
        };
        _tickets[seedTicket.TicketId] = seedTicket;
    }

    public IEnumerable<Ticket> GetTicketsForUser(string userId) =>
        _tickets.Values.Where(t => t.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));

    public Ticket? GetTicket(string ticketId) =>
        _tickets.GetValueOrDefault(ticketId);

    public Ticket CreateTicket(CreateTicketRequest request)
    {
        var id = Interlocked.Increment(ref _nextTicketId).ToString();
        var ticket = new Ticket
        {
            TicketId = id,
            UserId = request.UserId,
            Category = request.Category,
            Subject = request.Subject,
            Description = request.Description
        };
        _tickets[id] = ticket;
        return ticket;
    }

    public LeaveBalance? GetLeaveBalance(string userId) =>
        _leaveBalances.GetValueOrDefault(userId);
}
