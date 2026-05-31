using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin CRUD endpoints for the internal NSE trading calendar.
/// REQ-CALENDAR-001..006.
/// POST, PUT, and DELETE endpoints trigger a background Time Stop recompute
/// (REQ-STOP-003a).
/// </summary>
public static class TradingCalendarEndpoints
{
    private const string Tag = "Admin";

    public static IEndpointRouteBuilder MapTradingCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/calendar");

        // GET /api/v1/admin/calendar — list calendar entries
        // Optionally filter by ?from=YYYY-MM-DD&to=YYYY-MM-DD&session_type=normal
        // REQ-CALENDAR-002: view all session types and non-trading-day markers.
        admin.MapGet("/", async (
            ITradingCalendarRepository repo,
            string? from,
            string? to,
            string? sessionType,
            CancellationToken ct) =>
        {
            var entries = await repo.GetAllAsync(from, to, sessionType, ct);
            return Results.Ok(new CalendarEntriesResponse(entries));
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // GET /api/v1/admin/calendar/{date} — find entries for a specific date
        admin.MapGet("/{date}", async (
            string date,
            ITradingCalendarRepository repo,
            CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(date, out _))
                return Results.BadRequest(new CalendarErrorResponse("invalid_date_format"));

            var entries = await repo.FindByDateAsync(date, ct);
            return Results.Ok(new CalendarEntriesResponse(entries));
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // POST /api/v1/admin/calendar — create a new calendar entry
        // REQ-CALENDAR-002: supports normal, special, muhurat sessions and non-trading-day markers.
        // Session records require start/end times; non-trading-day markers require only date + optional holiday name.
        // REQ-STOP-003a: triggers Time Stop date recompute after creation.
        admin.MapPost("/", async (
            CreateCalendarEntryRequest request,
            ITradingCalendarRepository repo,
            TimeStopRecomputeService recomputeService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.SessionDate))
                return Results.BadRequest(new CalendarErrorResponse("session_date_required"));

            if (!DateOnly.TryParse(request.SessionDate, out _))
                return Results.BadRequest(new CalendarErrorResponse("invalid_session_date_format"));

            if (string.IsNullOrWhiteSpace(request.SessionType))
                return Results.BadRequest(new CalendarErrorResponse("session_type_required"));

            var validTypes = new[] { "normal", "special", "muhurat", "non_trading_day" };
            if (!validTypes.Contains(request.SessionType))
                return Results.BadRequest(new CalendarErrorResponse("invalid_session_type", ValidTypes: validTypes));

            // REQ-CALENDAR-002: validate based on type
            if (request.SessionType == "non_trading_day")
            {
                // Non-trading-day markers must NOT have session times; only optional holiday name.
                if (request.SessionStartTime is not null || request.SessionEndTime is not null)
                    return Results.BadRequest(new CalendarErrorResponse("non_trading_day_must_not_have_session_times"));
            }
            else
            {
                // Session records require start and end times.
                if (string.IsNullOrWhiteSpace(request.SessionStartTime))
                    return Results.BadRequest(new CalendarErrorResponse("session_start_time_required"));
                if (string.IsNullOrWhiteSpace(request.SessionEndTime))
                    return Results.BadRequest(new CalendarErrorResponse("session_end_time_required"));

                if (!TimeOnly.TryParse(request.SessionStartTime, out _))
                    return Results.BadRequest(new CalendarErrorResponse("invalid_session_start_time_format"));
                if (!TimeOnly.TryParse(request.SessionEndTime, out _))
                    return Results.BadRequest(new CalendarErrorResponse("invalid_session_end_time_format"));
            }

            // Check for duplicate: same date + same type
            var existing = await repo.FindByDateAndTypeAsync(request.SessionDate, request.SessionType, ct);
            if (existing is not null)
                return Results.Conflict(new CalendarErrorResponse("duplicate_calendar_entry", SessionDate: request.SessionDate, SessionType: request.SessionType));

            // Check: for non-session types, only one entry per date is allowed
            // (since the concept of "two different non-trading-day markers on same date" doesn't add value)
            if (request.SessionType == "non_trading_day")
            {
                var dateEntries = await repo.FindByDateAsync(request.SessionDate, ct);
                if (dateEntries.Count > 0)
                    return Results.Conflict(new CalendarErrorResponse("date_already_has_entry", SessionDate: request.SessionDate));
            }

            var now = DateTime.UtcNow;
            var doc = new TradingCalendarDocument
            {
                SessionDate = request.SessionDate,
                SessionType = request.SessionType,
                SessionStartTime = request.SessionStartTime,
                SessionEndTime = request.SessionEndTime,
                HolidayName = request.SessionType == "non_trading_day" ? request.HolidayName : null,
                CreatedAt = now,
                UpdatedAt = now
            };

            await repo.CreateAsync(doc, ct);

            // REQ-STOP-003a: trigger Time Stop recompute after calendar edit.
            recomputeService.Trigger();

            return Results.Created($"/api/v1/admin/calendar/{doc.Id}", doc);
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // PUT /api/v1/admin/calendar/{id} — update an existing calendar entry
        // REQ-STOP-003a: triggers Time Stop date recompute after update.
        admin.MapPut("/{id}", async (
            string id,
            UpdateCalendarEntryRequest request,
            ITradingCalendarRepository repo,
            TimeStopRecomputeService recomputeService,
            CancellationToken ct) =>
        {
            if (!MongoDB.Bson.ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new CalendarErrorResponse("invalid_id"));

            var validTypes = new[] { "normal", "special", "muhurat", "non_trading_day" };
            if (request.SessionType is not null && !validTypes.Contains(request.SessionType))
                return Results.BadRequest(new CalendarErrorResponse("invalid_session_type", ValidTypes: validTypes));

            await repo.UpdateAsync(
                objectId,
                sessionType: request.SessionType,
                sessionStartTime: request.SessionStartTime,
                sessionEndTime: request.SessionEndTime,
                holidayName: request.HolidayName,
                ct: ct);

            // REQ-STOP-003a: trigger Time Stop recompute after calendar edit.
            recomputeService.Trigger();

            return Results.Ok(new CalendarUpdateResponse("updated"));
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // GET /api/v1/admin/calendar/coverage — calendar coverage check
        // REQ-CALENDAR-007: detects weekdays in the next 30 days that have
        // neither a session record nor a non-trading-day marker.
        admin.MapGet("/coverage", async (
            ITradingCalendarRepository repo,
            CancellationToken ct) =>
        {
            // Today in Asia/Kolkata (IST)
            var istNow = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, IstTimeZone.Instance);
            var today = DateOnly.FromDateTime(istNow);
            var fromDate = today.ToString("yyyy-MM-dd");
            var toDate = today.AddDays(30).ToString("yyyy-MM-dd");

            // Fetch all entries in the 31-day window (today + 30).
            var entries = await repo.GetAllAsync(fromDate, toDate, null, ct);
            var coveredDates = new HashSet<string>(
                entries.Select(e => e.SessionDate));

            var unconfirmedDates = new List<string>();

            for (int i = 0; i <= 30; i++)
            {
                var date = today.AddDays(i);

                // Only weekdays (Monday through Friday).
                if (date.DayOfWeek >= DayOfWeek.Monday &&
                    date.DayOfWeek <= DayOfWeek.Friday)
                {
                    var dateStr = date.ToString("yyyy-MM-dd");
                    if (!coveredDates.Contains(dateStr))
                    {
                        unconfirmedDates.Add(dateStr);
                    }
                }
            }

            return Results.Ok(new CalendarCoverageResponse(
                UnconfirmedCount: unconfirmedDates.Count,
                EarliestUnconfirmedDate: unconfirmedDates.FirstOrDefault(),
                UnconfirmedDates: unconfirmedDates,
                CheckedFrom: fromDate,
                CheckedTo: toDate
            ));
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // DELETE /api/v1/admin/calendar/{id} — delete a calendar entry
        // REQ-STOP-003a: triggers Time Stop date recompute after deletion.
        admin.MapDelete("/{id}", async (
            string id,
            ITradingCalendarRepository repo,
            TimeStopRecomputeService recomputeService,
            CancellationToken ct) =>
        {
            if (!MongoDB.Bson.ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new CalendarErrorResponse("invalid_id"));

            await repo.DeleteAsync(objectId, ct);

            // REQ-STOP-003a: trigger Time Stop recompute after calendar edit.
            recomputeService.Trigger();

            return Results.Ok(new CalendarDeleteResponse("deleted"));
        })
        .RequireAuthorization()
        .WithTags(Tag);

        return app;
    }
}

/// <summary>Request body for POST /api/v1/admin/calendar.</summary>
public sealed record CreateCalendarEntryRequest
{
    /// <summary>Session date in Asia/Kolkata (ISO 8601, e.g. "2026-05-01").</summary>
    [Required] public string SessionDate { get; init; } = "";

    /// <summary>Session type: "normal", "special", "muhurat", or "non_trading_day".</summary>
    [Required] public string SessionType { get; init; } = "";

    /// <summary>Session start time in IST (HH:mm format). Required for session records.</summary>
    public string? SessionStartTime { get; init; }

    /// <summary>Session end time in IST (HH:mm format). Required for session records.</summary>
    public string? SessionEndTime { get; init; }

    /// <summary>Holiday name for non-trading-day markers (e.g. "Republic Day"). Optional.</summary>
    public string? HolidayName { get; init; }
}

/// <summary>Request body for PUT /api/v1/admin/calendar/{id}.</summary>
public sealed record UpdateCalendarEntryRequest
{
    /// <summary>New session type. Null = no change.</summary>
    public string? SessionType { get; init; }

    /// <summary>New session start time. Null = no change.</summary>
    public string? SessionStartTime { get; init; }

    /// <summary>New session end time. Null = no change.</summary>
    public string? SessionEndTime { get; init; }

    /// <summary>New holiday name. Null = no change.</summary>
    public string? HolidayName { get; init; }
}

/// <summary>Response for GET / and GET /{date} calendar endpoints.</summary>
public sealed record CalendarEntriesResponse(IEnumerable<TradingCalendarDocument> Entries);

/// <summary>Response for PUT calendar endpoint.</summary>
public sealed record CalendarUpdateResponse(string Status);

/// <summary>Response for GET /coverage calendar coverage check.</summary>
public sealed record CalendarCoverageResponse(
    int UnconfirmedCount,
    string? EarliestUnconfirmedDate,
    List<string> UnconfirmedDates,
    string CheckedFrom,
    string CheckedTo
);

/// <summary>Response for DELETE calendar endpoint.</summary>
public sealed record CalendarDeleteResponse(string Status);

/// <summary>Error response for calendar endpoints.
/// Optional properties use JsonIgnore to maintain identical JSON output
/// across different error shapes (simple errors, validation errors with
/// ValidTypes, and conflict errors with SessionDate/SessionType).</summary>
public sealed record CalendarErrorResponse(
    string Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string[]? ValidTypes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SessionDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SessionType = null
);
