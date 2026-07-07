using Helpdesk.Api.Models;
using Helpdesk.Api.Services;

namespace Helpdesk.Api.Tests;

public class HelpdeskStoreTests
{
    [Fact]
    public void GetTicket_ReturnsSeededTicket()
    {
        var store = new HelpdeskStore();

        var ticket = store.GetTicket("123");

        Assert.NotNull(ticket);
        Assert.Equal("alice", ticket!.UserId);
        Assert.Equal("In Progress", ticket.Status);
    }

    [Fact]
    public void GetTicket_ReturnsNullForUnknownId()
    {
        var store = new HelpdeskStore();

        Assert.Null(store.GetTicket("does-not-exist"));
    }

    [Fact]
    public void CreateTicket_NeverCollidesWithSeedData()
    {
        // Regression test: the generated ticket id counter must never reach the seeded ticket id ("123"),
        // otherwise CreateTicket would silently overwrite the seed ticket.
        var store = new HelpdeskStore();
        var seeded = store.GetTicket("123");

        for (var i = 0; i < 50; i++)
        {
            store.CreateTicket(new CreateTicketRequest("bob", "IT", "Subject", "Description"));
        }

        Assert.Same(seeded, store.GetTicket("123"));
    }

    [Fact]
    public void CreateTicket_AssignsUniqueIncreasingIds()
    {
        var store = new HelpdeskStore();

        var first = store.CreateTicket(new CreateTicketRequest("alice", "HR", "Subject 1", "Description 1"));
        var second = store.CreateTicket(new CreateTicketRequest("alice", "HR", "Subject 2", "Description 2"));

        Assert.NotEqual(first.TicketId, second.TicketId);
    }

    [Fact]
    public void GetTicketsForUser_IsCaseInsensitive()
    {
        var store = new HelpdeskStore();

        var tickets = store.GetTicketsForUser("ALICE").ToList();

        Assert.Contains(tickets, t => t.TicketId == "123");
    }

    [Theory]
    [InlineData("alice", 12, 7)]
    [InlineData("bob", 18, 10)]
    public void GetLeaveBalance_ReturnsSeededBalance(string userId, int vacationDays, int sickDays)
    {
        var store = new HelpdeskStore();

        var balance = store.GetLeaveBalance(userId);

        Assert.NotNull(balance);
        Assert.Equal(vacationDays, balance!.VacationDays);
        Assert.Equal(sickDays, balance.SickDays);
    }

    [Fact]
    public void GetLeaveBalance_ReturnsNullForUnknownUser()
    {
        var store = new HelpdeskStore();

        Assert.Null(store.GetLeaveBalance("unknown-user"));
    }
}
