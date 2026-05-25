using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Admin;

namespace SignalStack.Storage.Admin;

public sealed class MongoChaosExerciseRepository : IChaosExerciseRepository
{
    private readonly IMongoCollection<ChaosExerciseDocument> _collection;

    public MongoChaosExerciseRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ChaosExerciseDocument>("chaos_exercises");
    }

    public async Task<List<ChaosExerciseDocument>> ListAllAsync(CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<ChaosExerciseDocument>.Filter.Empty)
            .Sort(Builders<ChaosExerciseDocument>.Sort.Descending(d => d.ExecutedAt))
            .ToListAsync(ct);
    }

    public async Task<ChaosExerciseDocument?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<ChaosExerciseDocument>.Filter.Eq(d => d.Id, id))
            .FirstOrDefaultAsync(ct);
    }

    public async Task CreateAsync(ChaosExerciseDocument document, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task UpdateAsync(ObjectId id, string? outcome = null, string? findings = null,
        string? remediation = null, CancellationToken ct = default)
    {
        var filter = Builders<ChaosExerciseDocument>.Filter.Eq(d => d.Id, id);

        var updateDefs = new List<UpdateDefinition<ChaosExerciseDocument>>
        {
            Builders<ChaosExerciseDocument>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow)
        };

        if (outcome is not null)
            updateDefs.Add(Builders<ChaosExerciseDocument>.Update.Set(d => d.Outcome, outcome));
        if (findings is not null)
            updateDefs.Add(Builders<ChaosExerciseDocument>.Update.Set(d => d.Findings, findings));
        if (remediation is not null)
            updateDefs.Add(Builders<ChaosExerciseDocument>.Update.Set(d => d.Remediation, remediation));

        var combined = Builders<ChaosExerciseDocument>.Update.Combine(updateDefs);
        await _collection.UpdateOneAsync(filter, combined, cancellationToken: ct);
    }

    public async Task<bool> AllFiveExercisesPassedOnceAsync(CancellationToken ct = default)
    {
        // Count distinct exercise numbers that have at least one pass outcome.
        var pipeline = new BsonDocument[]
        {
            new()
            {
                ["$match"] = new BsonDocument
                {
                    ["outcome"] = new BsonDocument("$in", new BsonArray { "pass", "walkthrough" })
                }
            },
            new()
            {
                ["$group"] = new BsonDocument
                {
                    ["_id"] = "$exercise_number"
                }
            },
            new()
            {
                ["$count"] = "distinct_count"
            }
        };

        var result = await _collection
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .FirstOrDefaultAsync(ct);

        var count = result?.GetValue("distinct_count", 0).AsInt32 ?? 0;
        return count >= 5;
    }
}
