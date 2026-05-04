using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Admin;

public sealed class MongoPenetrationTestRepository : IPenetrationTestRepository
{
    private readonly IMongoCollection<PenetrationTestDocument> _collection;

    public MongoPenetrationTestRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<PenetrationTestDocument>("penetration_tests");
    }

    public async Task<List<PenetrationTestDocument>> ListAllAsync(CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<PenetrationTestDocument>.Filter.Empty)
            .Sort(Builders<PenetrationTestDocument>.Sort.Descending(d => d.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<PenetrationTestDocument?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<PenetrationTestDocument>.Filter.Eq(d => d.Id, id))
            .FirstOrDefaultAsync(ct);
    }

    public async Task CreateAsync(PenetrationTestDocument document, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task UpdateAsync(
        ObjectId id,
        string? vendorName = null,
        List<string>? scope = null,
        DateTime? scheduledDate = null,
        DateTime? completedDate = null,
        string? status = null,
        string? engagementRef = null,
        CancellationToken ct = default)
    {
        var filter = Builders<PenetrationTestDocument>.Filter.Eq(d => d.Id, id);

        var updateDefs = new List<UpdateDefinition<PenetrationTestDocument>>
        {
            Builders<PenetrationTestDocument>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow)
        };

        if (vendorName is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(d => d.VendorName, vendorName));

        if (scope is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(d => d.Scope, scope));

        if (scheduledDate is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(d => d.ScheduledDate, scheduledDate));

        if (completedDate is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(d => d.CompletedDate, completedDate));

        if (status is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(d => d.Status, status));

        if (engagementRef is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(d => d.EngagementRef, engagementRef));

        var combined = Builders<PenetrationTestDocument>.Update.Combine(updateDefs);
        await _collection.UpdateOneAsync(filter, combined, cancellationToken: ct);
    }

    public async Task AddFindingAsync(ObjectId testId, PenetrationTestFinding finding, CancellationToken ct = default)
    {
        var filter = Builders<PenetrationTestDocument>.Filter.Eq(d => d.Id, testId);
        var update = Builders<PenetrationTestDocument>.Update
            .Push(d => d.Findings, finding)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task UpdateFindingAsync(
        ObjectId testId,
        string findingId,
        string? status = null,
        string? remediationNotes = null,
        CancellationToken ct = default)
    {
        var filter = Builders<PenetrationTestDocument>.Filter.And(
            Builders<PenetrationTestDocument>.Filter.Eq(d => d.Id, testId),
            Builders<PenetrationTestDocument>.Filter.Eq("findings.finding_id", findingId));

        var updateDefs = new List<UpdateDefinition<PenetrationTestDocument>>
        {
            Builders<PenetrationTestDocument>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow),
            Builders<PenetrationTestDocument>.Update.Set(
                "findings.$.updated_at", DateTime.UtcNow)
        };

        if (status is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(
                "findings.$.status", status));

        if (remediationNotes is not null)
            updateDefs.Add(Builders<PenetrationTestDocument>.Update.Set(
                "findings.$.remediation_notes", remediationNotes));

        var combined = Builders<PenetrationTestDocument>.Update.Combine(updateDefs);
        await _collection.UpdateOneAsync(filter, combined, cancellationToken: ct);
    }

    public async Task<long> CountUnresolvedHighCriticalFindingsAsync(ObjectId testId, CancellationToken ct = default)
    {
        var filter = Builders<PenetrationTestDocument>.Filter.And(
            Builders<PenetrationTestDocument>.Filter.Eq(d => d.Id, testId),
            Builders<PenetrationTestDocument>.Filter.ElemMatch(d => d.Findings,
                Builders<PenetrationTestFinding>.Filter.And(
                    Builders<PenetrationTestFinding>.Filter.In(f => f.Severity, ["critical", "high"]),
                    Builders<PenetrationTestFinding>.Filter.Nin(f => f.Status, ["remediated", "false_positive"]))));

        var doc = await _collection.Find(filter).FirstOrDefaultAsync(ct);
        if (doc?.Findings is null)
            return 0;

        return doc.Findings.Count(f =>
            (f.Severity == "critical" || f.Severity == "high") &&
            f.Status is not ("remediated" or "false_positive"));
    }
}
