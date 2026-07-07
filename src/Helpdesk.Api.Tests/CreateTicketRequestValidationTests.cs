using Helpdesk.Api.Models;

namespace Helpdesk.Api.Tests;

public class CreateTicketRequestValidationTests
{
    [Fact]
    public void Validate_ReturnsNoErrors_ForValidRequest()
    {
        var request = new CreateTicketRequest("alice", "IT", "Laptop issue", "My laptop won't boot.");

        Assert.Empty(request.Validate());
    }

    [Theory]
    [InlineData("Finance")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_RejectsInvalidCategory(string? category)
    {
        var request = new CreateTicketRequest("alice", category!, "Subject", "Description");

        var errors = request.Validate();

        Assert.Contains(errors, e => e.Contains("category", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("it")]
    [InlineData("HR")]
    public void Validate_AcceptsCategoryCaseInsensitively(string category)
    {
        var request = new CreateTicketRequest("alice", category, "Subject", "Description");

        Assert.Empty(request.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingUserId()
    {
        var request = new CreateTicketRequest("", "IT", "Subject", "Description");

        var errors = request.Validate();

        Assert.Contains(errors, e => e.Contains("userId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsOverlyLongSubject()
    {
        var request = new CreateTicketRequest("alice", "IT", new string('x', 201), "Description");

        var errors = request.Validate();

        Assert.Contains(errors, e => e.Contains("subject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsOverlyLongDescription()
    {
        var request = new CreateTicketRequest("alice", "IT", "Subject", new string('x', 4001));

        var errors = request.Validate();

        Assert.Contains(errors, e => e.Contains("description", StringComparison.OrdinalIgnoreCase));
    }
}
