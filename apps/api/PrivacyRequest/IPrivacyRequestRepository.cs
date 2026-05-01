namespace SignalStack.Api.PrivacyRequest;

/// <summary>
/// Abstraction over the <c>privacy_requests</c> MongoDB collection.
/// REQ-PRIVACY-004: DSAR ticket lifecycle (access, correction, erasure, grievance).
/// </summary>
public interface IPrivacyRequestRepository
{
    /// <summary>Returns all DSAR tickets, ordered by created_at descending.</summary>
    Task<List<PrivacyRequestDocument>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns a single DSAR ticket by its ticket_id, or null if not found.</summary>
    Task<PrivacyRequestDocument?> FindByTicketIdAsync(string ticketId, CancellationToken ct = default);

    /// <summary>Creates a new DSAR ticket. Returns the generated ticket_id.</summary>
    Task<string> CreateAsync(PrivacyRequestDocument request, CancellationToken ct = default);

    /// <summary>
    /// Updates the ticket status and sets acknowledged_at.
    /// Only valid for tickets in "open" status.
    /// Returns false if the ticket is not in "open" status.
    /// </summary>
    Task<bool> AcknowledgeAsync(string ticketId, DateTime acknowledgedAt, CancellationToken ct = default);

    /// <summary>
    /// Updates the ticket status to a new value.
    /// Returns false if the ticket_id is not found.
    /// </summary>
    Task<bool> UpdateStatusAsync(string ticketId, string newStatus, CancellationToken ct = default);

    /// <summary>
    /// Marks the ticket as completed with resolution notes.
    /// Returns false if the ticket_id is not found.
    /// </summary>
    Task<bool> CompleteAsync(string ticketId, string resolutionNotes, DateTime completedAt, CancellationToken ct = default);

    /// <summary>
    /// Returns the next available ticket sequence number (for generating "DSAR-{N}" ticket IDs).
    /// </summary>
    Task<long> GetNextSequenceAsync(CancellationToken ct = default);
}
