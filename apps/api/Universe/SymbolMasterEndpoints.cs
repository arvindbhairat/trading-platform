using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SignalStack.Storage.Universe;

namespace SignalStack.Api.Universe;

public static class SymbolMasterEndpoints
{
    private const string Tag = "Universe";

    public static RouteGroupBuilder MapSymbolMasterEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/v1/universe/symbols — list all symbols, optional ?archived=true|false filter
        // or ?q=RELIANCE for autocomplete search (matches symbol and company name).
        group.MapGet("/symbols", async (
            ISymbolMasterRepository repo,
            bool? archived,
            string? q,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(q))
            {
                var results = await repo.SearchAsync(q, ct: ct);
                return Results.Ok(results);
            }

            var symbols = await repo.GetAllAsync(archived, ct);
            return Results.Ok(symbols);
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // GET /api/v1/universe/symbols/{symbol} — find by trading symbol
        group.MapGet("/symbols/{symbol}", async (
            string symbol,
            ISymbolMasterRepository repo,
            CancellationToken ct) =>
        {
            var doc = await repo.FindBySymbolAsync(symbol, ct);
            return doc is not null ? Results.Ok(doc) : Results.NotFound();
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // POST /api/v1/universe/symbols — create a new symbol master entry
        // REQ-UNIV-001..010, REQ-HIST-005/006/007/008/008a
        group.MapPost("/symbols", async (
            CreateSymbolRequest request,
            ISymbolMasterRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
                return Results.BadRequest(new ErrorResponse("Symbol is required."));

            if (string.IsNullOrWhiteSpace(request.Isin))
                return Results.BadRequest(new ErrorResponse("ISIN code is required."));

            // Check symbol/ISIN uniqueness (REQ-UNIV-002)
            var hasConflict = await repo.HasConflictAsync(
                request.Symbol.Trim(), request.Isin.Trim(), null, ct);
            if (hasConflict)
                return Results.Conflict(new ErrorResponse("Symbol or ISIN already exists."));

            // Generate deterministic sql_table_name_suffix (REQ-HIST-006/007)
            string suffix;
            try
            {
                suffix = SqlTableNameSuffixGenerator.Generate(request.Symbol);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }

            // Check suffix collision (REQ-HIST-008a)
            var existingBySuffix = await repo.FindBySuffixAsync(suffix, ct);
            if (existingBySuffix is not null)
                return Results.Conflict(new SymbolConflictResponse(
                    $"sql_table_name_suffix '{suffix}' already in use by symbol '{existingBySuffix.Symbol}'.",
                    existingBySuffix.Symbol,
                    suffix));

            var now = DateTime.UtcNow;
            var doc = new SymbolMasterDocument
            {
                Symbol = request.Symbol.Trim().ToUpperInvariant(),
                CompanyName = request.CompanyName.Trim(),
                Industry = request.Industry.Trim(),
                Series = (request.Series ?? "EQ").Trim().ToUpperInvariant(),
                Isin = request.Isin.Trim(),
                SqlTableNameSuffix = suffix,
                LotSize = request.LotSize.GetValueOrDefault(1),
                IsArchived = false,
                ScanExcluded = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            await repo.CreateAsync(doc, ct);
            return Results.Created($"/api/v1/universe/symbols/{doc.Symbol}", doc);
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // PATCH /api/v1/universe/symbols/{symbol} — update mutable fields
        // (industry, is_archived, scan_excluded). REQ-UNIV-002a, REQ-UNIV-006, REQ-UNIV-009.
        group.MapPatch("/symbols/{symbol}", async (
            string symbol,
            UpdateSymbolRequest request,
            ISymbolMasterRepository repo,
            CancellationToken ct) =>
        {
            var doc = await repo.FindBySymbolAsync(symbol, ct);
            if (doc is null)
                return Results.NotFound();

            await repo.UpdateMetadataAsync(
                doc.Id,
                industry: request.Industry,
                isArchived: request.IsArchived,
                scanExcluded: request.ScanExcluded,
                ct: ct);

            return Results.Ok(new StatusResponse("updated"));
        })
        .RequireAuthorization()
        .WithTags(Tag);

        // GET /api/v1/universe/count — total count, optional ?archived=true|false
        group.MapGet("/count", async (
            ISymbolMasterRepository repo,
            bool? archived,
            CancellationToken ct) =>
        {
            var count = await repo.CountAsync(archived, ct);
            return Results.Ok(new CountResponse(count));
        })
        .RequireAuthorization()
        .WithTags(Tag);

        return group;
    }
}

/// <summary>Request body for POST /api/v1/universe/symbols.</summary>
public sealed record CreateSymbolRequest
{
    /// <summary>Trading symbol on NSE (e.g. "RELIANCE").</summary>
    [Required] public string Symbol { get; init; } = "";

    /// <summary>Company name from the Nifty 500 CSV.</summary>
    [Required] public string CompanyName { get; init; } = "";

    /// <summary>Industry classification from the CSV.</summary>
    [Required] public string Industry { get; init; } = "";

    /// <summary>NSE series. Defaults to "EQ".</summary>
    public string? Series { get; init; }

    /// <summary>ISIN code for the security.</summary>
    [Required] public string Isin { get; init; } = "";

    /// <summary>
    /// NSE minimum lot size for equity CNC orders.
    /// Defaults to 1 (REQ-UNIV-002).
    /// </summary>
    public int? LotSize { get; init; }
}

/// <summary>Request body for PATCH /api/v1/universe/symbols/{symbol}.</summary>
public sealed record UpdateSymbolRequest
{
    /// <summary>New industry classification. Null = no change.</summary>
    public string? Industry { get; init; }

    /// <summary>True to archive the symbol. Null = no change.</summary>
    public bool? IsArchived { get; init; }

    /// <summary>True to exclude from scans. Null = no change.</summary>
    public bool? ScanExcluded { get; init; }
}

// ── Response records ───────────────────────────────────────────────────────

public sealed record ErrorResponse(string Error);

public sealed record SymbolConflictResponse(
    string Error,
    string CollidingSymbol,
    string GeneratedSuffix
);

public sealed record StatusResponse(string Status);

public sealed record CountResponse(long Count);
