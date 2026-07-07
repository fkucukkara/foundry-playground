namespace Helpdesk.Api.Models;

public record Ticket
{
    public required string TicketId { get; init; }
    public required string UserId { get; init; }
    public required string Category { get; init; } // "IT" or "HR"
    public required string Subject { get; init; }
    public required string Description { get; init; }
    public string Status { get; init; } = "Open";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record CreateTicketRequest(string UserId, string Category, string Subject, string Description)
{
    private static readonly string[] ValidCategories = ["IT", "HR"];

    /// <summary>Validates the request, returning a list of human-readable error messages (empty if valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(UserId))
        {
            errors.Add("userId is required.");
        }

        if (string.IsNullOrWhiteSpace(Category) || !ValidCategories.Contains(Category, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("category must be 'IT' or 'HR'.");
        }

        if (string.IsNullOrWhiteSpace(Subject))
        {
            errors.Add("subject is required.");
        }
        else if (Subject.Length > 200)
        {
            errors.Add("subject must be 200 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            errors.Add("description is required.");
        }
        else if (Description.Length > 4000)
        {
            errors.Add("description must be 4000 characters or fewer.");
        }

        return errors;
    }
}

public record LeaveBalance(string UserId, int VacationDays, int SickDays);
