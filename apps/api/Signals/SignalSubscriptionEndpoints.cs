using System.Security.Claims;
using MongoDB.Bson;

namespace SignalStack.Api.Signals;

public static class SignalSubscriptionEndpoints
{
    private const string Tag = "Signals";

    public static IEndpointRouteBuilder MapSignalSubscriptionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var subs = app.MapGroup("/api/v1/signals/subscriptions");

        // ── GET /api/v1/signals/subscriptions ─────────────────────────────
        // List all subscriptions for the authenticated user.
        // REQ-STRAT-007b: paused subscriptions labelled inactive.
        subs.MapGet("/", async (
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var list = await repo.GetByUserIdAsync(userId, context.RequestAborted);
            return Results.Ok(list.Select(MapToDto));
        }).RequireAuthorization().WithTags(Tag);

        // ── GET /api/v1/signals/subscriptions/{id} ────────────────────────
        // Get a single subscription by ID.
        subs.MapGet("/{id}", async (
            string id,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid subscription ID." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            return sub is not null ? Results.Ok(MapToDto(sub)) : Results.NotFound();
        }).RequireAuthorization().WithTags(Tag);

        // ── POST /api/v1/signals/subscriptions ────────────────────────────
        // Create a new Signal Subscription with initial RME version.
        // REQ-STRAT-007a: default status is active.
        // REQ-STRAT-017b: first version is created as live immediately.
        subs.MapPost("/", async (
            CreateSubscriptionRequest request,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Subscription name is required." });
            if (string.IsNullOrWhiteSpace(request.SignalTypeId))
                return Results.BadRequest(new { error = "Signal type is required." });

            var now = DateTime.UtcNow;
            var versionId = ObjectId.GenerateNewId();
            var initialVersion = new RmeConfigurationVersion
            {
                VersionId = versionId,
                VersionNumber = 1,
                RmeConfiguration = request.RmeConfiguration ?? [],
                EffectiveFrom = now,
                CreatedAt = now
            };

            var doc = new SignalSubscriptionDocument
            {
                UserId = userId,
                Name = request.Name.Trim(),
                SignalTypeId = request.SignalTypeId,
                Timeframe = request.Timeframe ?? "daily",
                Parameters = request.Parameters ?? [],
                Status = SubscriptionStatus.Active,
                Versions = [initialVersion],
                CurrentVersionIndex = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            await repo.CreateAsync(doc, context.RequestAborted);

            return Results.Created(
                $"/api/v1/signals/subscriptions/{doc.Id}",
                MapToDto(doc));
        }).RequireAuthorization().WithTags(Tag);

        // ── PUT /api/v1/signals/subscriptions/{id} ────────────────────────
        // Edit a subscription's signal parameters (name, timeframe, parameters).
        // RME config changes use the versioned copy-on-write path instead.
        subs.MapPut("/{id}", async (
            string id,
            UpdateSubscriptionRequest request,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid subscription ID." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            if (sub is null)
                return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(request.Name))
                sub.Name = request.Name.Trim();
            if (!string.IsNullOrWhiteSpace(request.Timeframe))
                sub.Timeframe = request.Timeframe;
            if (request.Parameters is not null && request.Parameters.ElementCount > 0)
                sub.Parameters = request.Parameters;

            sub.UpdatedAt = DateTime.UtcNow;
            await repo.ReplaceAsync(sub, context.RequestAborted);

            return Results.Ok(MapToDto(sub));
        }).RequireAuthorization().WithTags(Tag);

        // ── POST /api/v1/signals/subscriptions/{id}/pause ─────────────────
        // Pause a Signal Subscription. REQ-STRAT-007a:
        //   - EODSR will skip this subscription
        //   - Open positions continue RME monitoring (unchanged here, enforced by EODSR)
        //   - PendingEntry positions must be suspended (enforced by position service)
        //   - Pause/resume during in-flight EODSR does not affect the run (EODSR reads
        //     state once at start — REQ-STRAT-007b)
        subs.MapPost("/{id}/pause", async (
            string id,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid subscription ID." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            if (sub is null)
                return Results.NotFound();

            if (sub.Status == SubscriptionStatus.Paused)
                return Results.Ok(new { status = "already_paused", message = "Subscription is already paused." });

            await repo.UpdateStatusAsync(oid, SubscriptionStatus.Paused, DateTime.UtcNow, context.RequestAborted);

            // Note: PendingEntry → Suspended transition for positions associated
            // with this subscription is handled by the position service (P6-Tn).
            // The pause endpoint sets the subscription state; the position service
            // observes the change and transitions eligible PendingEntry positions.

            return Results.Ok(new { status = "paused" });
        }).RequireAuthorization().WithTags(Tag);

        // ── POST /api/v1/signals/subscriptions/{id}/resume ────────────────
        // Resume a paused Signal Subscription. REQ-STRAT-007a/007b:
        //   - Subscription participates in next eligible EODSR run
        //   - Previously suspended PendingEntry positions remain Suspended
        //     (not automatically reinstated — per REQ-STRAT-007a)
        subs.MapPost("/{id}/resume", async (
            string id,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid subscription ID." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            if (sub is null)
                return Results.NotFound();

            if (sub.Status == SubscriptionStatus.Active)
                return Results.Ok(new { status = "already_active", message = "Subscription is already active." });

            await repo.UpdateStatusAsync(oid, SubscriptionStatus.Active, DateTime.UtcNow, context.RequestAborted);

            return Results.Ok(new { status = "active" });
        }).RequireAuthorization().WithTags(Tag);

        // ── POST /api/v1/signals/subscriptions/{id}/versions ──────────────
        // Create a new RME configuration version (copy-on-write).
        // REQ-STRAT-017b: new version has effective_from = next EODSR run (or now
        // if no EODSR is scheduled). The previous version remains governing for
        // existing positions.
        subs.MapPost("/{id}/versions", async (
            string id,
            CreateVersionRequest request,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid subscription ID." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            if (sub is null)
                return Results.NotFound();

            if (request.RmeConfiguration is null || request.RmeConfiguration.ElementCount == 0)
                return Results.BadRequest(new { error = "RME configuration is required." });

            // Determine the next version number.
            var nextVersionNumber = sub.Versions.Count > 0
                ? sub.Versions.Max(v => v.VersionNumber) + 1
                : 1;

            var now = DateTime.UtcNow;
            var newVersion = new RmeConfigurationVersion
            {
                VersionId = ObjectId.GenerateNewId(),
                VersionNumber = nextVersionNumber,
                RmeConfiguration = request.RmeConfiguration,
                // effective_from: defaults to now (immediate). When EODSR is available
                // (P5-T1), this will be set to the next scheduled EODSR run time.
                EffectiveFrom = request.EffectiveFrom ?? now,
                CreatedAt = now
            };

            // The new version index is sub.Versions.Count (before push).
            var newPendingIndex = sub.Versions.Count;
            await repo.AddVersionAsync(oid, newVersion, newPendingIndex, now, context.RequestAborted);

            return Results.Ok(new
            {
                version_id = newVersion.VersionId.ToString(),
                version_number = newVersion.VersionNumber,
                status = "pending",
                effective_from = newVersion.EffectiveFrom.ToString("o"),
                message = "New RME configuration version created. It will govern the next eligible EODSR run."
            });
        }).RequireAuthorization().WithTags(Tag);

        // ── GET /api/v1/signals/subscriptions/{id}/versions ──────────────
        // List all RME configuration versions for a subscription.
        // REQ-STRAT-017b: version history with live/pending/superseded markers.
        subs.MapGet("/{id}/versions", async (
            string id,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid subscription ID." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            if (sub is null)
                return Results.NotFound();

            var liveVersion = sub.CurrentVersionIndex >= 0 && sub.CurrentVersionIndex < sub.Versions.Count
                ? sub.Versions[sub.CurrentVersionIndex]
                : null;

            var versionDtos = sub.Versions.Select((v, idx) => new
            {
                version_id = v.VersionId.ToString(),
                version_number = v.VersionNumber,
                status = idx == sub.PendingVersionIndex ? "pending"
                    : idx == sub.CurrentVersionIndex ? "live"
                    : "superseded",
                effective_from = v.EffectiveFrom.ToString("o"),
                created_at = v.CreatedAt.ToString("o"),
                is_live = idx == sub.CurrentVersionIndex,
                is_pending = idx == sub.PendingVersionIndex
            });

            return Results.Ok(new
            {
                versions = versionDtos,
                current_version = liveVersion?.VersionNumber,
                pending_version_index = sub.PendingVersionIndex
            });
        }).RequireAuthorization().WithTags(Tag);

        // ── POST /api/v1/signals/subscriptions/{id}/versions/{versionId}/discard ──
        // Discard a pending version before it takes effect.
        // REQ-STRAT-017b: reverts future EODSR runs to the previous live version.
        subs.MapPost("/{id}/versions/{versionId}/discard", async (
            string id,
            string versionId,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid) || !ObjectId.TryParse(versionId, out var vid))
                return Results.BadRequest(new { error = "Invalid ID format." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            if (sub is null)
                return Results.NotFound();

            if (sub.PendingVersionIndex is null)
                return Results.BadRequest(new { error = "No pending version to discard." });

            var pendingVersion = sub.Versions[sub.PendingVersionIndex.Value];
            if (pendingVersion.VersionId != vid)
                return Results.BadRequest(new { error = "Specified version is not the current pending version." });

            await repo.DiscardPendingVersionAsync(oid, DateTime.UtcNow, context.RequestAborted);

            return Results.Ok(new { status = "discarded", message = "Pending version discarded." });
        }).RequireAuthorization().WithTags(Tag);

        // ── DELETE /api/v1/signals/subscriptions/{id} ────────────────────
        // Permanently deletes a Signal Subscription.
        // REQ-STRAT-017a: may not be deleted while active positions reference it
        // (enforced by position service in later phases).
        subs.MapDelete("/{id}", async (
            string id,
            HttpContext context,
            ISignalSubscriptionRepository repo) =>
        {
            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid subscription ID." });

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sub = await repo.GetByIdAndUserAsync(oid, userId, context.RequestAborted);
            if (sub is null)
                return Results.NotFound();

            await repo.DeleteAsync(oid, context.RequestAborted);
            return Results.Ok(new { status = "deleted" });
        }).RequireAuthorization().WithTags(Tag);

        return app;
    }

    // ── DTO mapping ───────────────────────────────────────────────────────

    private static object MapToDto(SignalSubscriptionDocument sub)
    {
        var liveVersion = sub.CurrentVersionIndex >= 0 && sub.CurrentVersionIndex < sub.Versions.Count
            ? sub.Versions[sub.CurrentVersionIndex]
            : null;

        return new
        {
            id = sub.Id.ToString(),
            user_id = sub.UserId,
            name = sub.Name,
            signal_type_id = sub.SignalTypeId,
            timeframe = sub.Timeframe,
            parameters = sub.Parameters,
            status = sub.Status,
            is_paused = sub.Status == SubscriptionStatus.Paused,
            current_version = liveVersion is not null ? new
            {
                version_id = liveVersion.VersionId.ToString(),
                version_number = liveVersion.VersionNumber,
                effective_from = liveVersion.EffectiveFrom.ToString("o")
            } : null,
            has_pending_version = sub.PendingVersionIndex.HasValue,
            version_count = sub.Versions.Count,
            created_at = sub.CreatedAt.ToString("o"),
            updated_at = sub.UpdatedAt.ToString("o")
        };
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────

public sealed record CreateSubscriptionRequest
{
    public string Name { get; init; } = "";
    public string SignalTypeId { get; init; } = "";
    public string? Timeframe { get; init; }
    public BsonDocument? Parameters { get; init; }
    public BsonDocument? RmeConfiguration { get; init; }
}

public sealed record UpdateSubscriptionRequest
{
    public string? Name { get; init; }
    public string? Timeframe { get; init; }
    public BsonDocument? Parameters { get; init; }
}

public sealed record CreateVersionRequest
{
    public BsonDocument? RmeConfiguration { get; init; }
    public DateTime? EffectiveFrom { get; init; }
}
