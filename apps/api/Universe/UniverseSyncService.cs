using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;

namespace SignalStack.Api.Universe;

/// <summary>
/// Core business logic for the Universe Sync flow.
/// REQ-UNIV-011..020.
/// </summary>
public sealed class UniverseSyncService
{
    private readonly ISymbolMasterRepository _symbolRepo;
    private readonly IUniverseUploadRepository _uploadRepo;
    private readonly ILogger<UniverseSyncService> _logger;

    public UniverseSyncService(
        ISymbolMasterRepository symbolRepo,
        IUniverseUploadRepository uploadRepo,
        ILogger<UniverseSyncService> logger)
    {
        _symbolRepo = symbolRepo;
        _uploadRepo = uploadRepo;
        _logger = logger;
    }

    /// <summary>Parsed CSV row before classification.</summary>
    public sealed record CsvRow(
        string CompanyName,
        string Industry,
        string Symbol,
        string Series,
        string IsinCode);

    /// <summary>Result of the diff preview step. REQ-UNIV-017.</summary>
    public sealed record DiffPreview
    {
        /// <summary>Symbols to be added (net-new, absent from current master).</summary>
        public required List<DiffAddEntry> Adds { get; init; }
        /// <summary>Symbols to be archived (present in master, absent from CSV, not resolved as rename).</summary>
        public required List<DiffArchiveEntry> Archives { get; init; }
        /// <summary>Symbols whose Industry classification would change.</summary>
        public required List<DiffIndustryChange> IndustryChanges { get; init; }
        /// <summary>Rows skipped because Series != EQ.</summary>
        public required List<DiffSkippedEntry> Skipped { get; init; }
        /// <summary>Candidate renames detected by ISIN match. REQ-UNIV-020.</summary>
        public required List<DiffRenameCandidate> RenameCandidates { get; init; }
        /// <summary>Total parsed CSV rows.</summary>
        public required int TotalRows { get; init; }
        /// <summary>Rows accepted (Series == EQ).</summary>
        public required int AcceptedRows { get; init; }
        /// <summary>SHA-256 of the raw CSV content.</summary>
        public required string FileHash { get; init; }
    }

    public sealed record DiffAddEntry
    {
        public required string Symbol { get; init; }
        public required string CompanyName { get; init; }
        public required string Industry { get; init; }
        public required string Isin { get; init; }
        public required string SqlTableNameSuffix { get; init; }
    }

    public sealed record DiffArchiveEntry
    {
        public required string Symbol { get; init; }
        public required string CompanyName { get; init; }
        public required string Isin { get; init; }
    }

    public sealed record DiffIndustryChange
    {
        public required string Symbol { get; init; }
        public required string PreviousIndustry { get; init; }
        public required string NewIndustry { get; init; }
    }

    public sealed record DiffSkippedEntry
    {
        public required string Symbol { get; init; }
        public required string CompanyName { get; init; }
        public required string Series { get; init; }
    }

    public sealed record DiffRenameCandidate
    {
        public required string OldSymbol { get; init; }
        public required string NewSymbol { get; init; }
        public required string Isin { get; init; }
        public required string CompanyName { get; init; }
        public required string Industry { get; init; }
    }

    /// <summary>
    /// Request to commit an upload after the admin has reviewed the diff and resolved renames.
    /// </summary>
    public sealed record CommitRequest
    {
        public required string AdminUserId { get; init; }
        public required string FileHash { get; init; }
        public required int TotalRows { get; init; }
        public required List<DiffAddEntry> Adds { get; init; }
        public required List<DiffArchiveEntry> Archives { get; init; }
        public required List<DiffIndustryChange> IndustryChanges { get; init; }
        public required List<DiffSkippedEntry> Skipped { get; init; }
        public required List<RenameResolution> RenameResolutions { get; init; }
        /// <summary>Typed confirmation phrase — required when archive count > threshold.</summary>
        public string? ConfirmationPhrase { get; init; }
        /// <summary>Written justification — required when archive count > threshold.</summary>
        public string? Justification { get; init; }
    }

    public sealed record RenameResolution
    {
        public required string OldSymbol { get; init; }
        public required string NewSymbol { get; init; }
        public required string Isin { get; init; }
        /// <summary>"approved" or "rejected".</summary>
        public required string Resolution { get; init; }
        public string CompanyName { get; init; } = "";
        public string Industry { get; init; } = "";
    }

    /// <summary>Result of a commit operation.</summary>
    public sealed record CommitResult
    {
        public required bool Success { get; init; }
        public string? Error { get; init; }
        public ObjectId? UploadId { get; init; }
        public List<string> NewSymbols { get; init; } = [];
        public List<string> ArchivedSymbols { get; init; } = [];
    }

    /// <summary>Result of a rollback operation.</summary>
    public sealed record RollbackResult
    {
        public required bool Success { get; init; }
        public string? Error { get; init; }
    }

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses a CSV upload and produces a diff preview against the current symbol master.
    /// REQ-UNIV-017, REQ-UNIV-020.
    /// </summary>
    public async Task<DiffPreview> GeneratePreviewAsync(
        string csvContent, CancellationToken ct = default)
    {
        var rows = ParseCsv(csvContent);
        var fileHash = ComputeHash(csvContent);
        var currentSymbols = await _symbolRepo.GetAllAsync(archived: null, ct);
        var currentBySymbol = currentSymbols.ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);
        var currentByIsin = currentSymbols
            .Where(s => !s.IsArchived)
            .GroupBy(s => s.Isin)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var adds = new List<DiffAddEntry>();
        var archives = new List<DiffArchiveEntry>();
        var industryChanges = new List<DiffIndustryChange>();
        var skipped = new List<DiffSkippedEntry>();
        var renameCandidates = new List<DiffRenameCandidate>();

        var uploadedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedRenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: classify each CSV row.
        foreach (var row in rows)
        {
            if (!string.Equals(row.Series, "EQ", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new DiffSkippedEntry
                {
                    Symbol = row.Symbol,
                    CompanyName = row.CompanyName,
                    Series = row.Series
                });
                continue;
            }

            uploadedSymbols.Add(row.Symbol);

            if (currentBySymbol.TryGetValue(row.Symbol, out var existing))
            {
                // Symbol exists — check for industry change.
                if (!string.Equals(existing.Industry, row.Industry, StringComparison.Ordinal))
                {
                    industryChanges.Add(new DiffIndustryChange
                    {
                        Symbol = row.Symbol,
                        PreviousIndustry = existing.Industry,
                        NewIndustry = row.Industry
                    });
                }
            }
            else
            {
                // Symbol is not in the current master — check rename candidate.
                if (currentByIsin.TryGetValue(row.IsinCode, out var isinMatch))
                {
                    // The ISIN matches an active symbol whose Symbol is not in the upload.
                    renameCandidates.Add(new DiffRenameCandidate
                    {
                        OldSymbol = isinMatch.Symbol,
                        NewSymbol = row.Symbol,
                        Isin = row.IsinCode,
                        CompanyName = row.CompanyName,
                        Industry = row.Industry
                    });
                    resolvedRenames.Add(row.Symbol);
                    resolvedRenames.Add(isinMatch.Symbol);
                }
                else
                {
                    // Genuinely new symbol.
                    var suffix = SqlTableNameSuffixGenerator.Generate(row.Symbol);
                    adds.Add(new DiffAddEntry
                    {
                        Symbol = row.Symbol,
                        CompanyName = row.CompanyName,
                        Industry = row.Industry,
                        Isin = row.IsinCode,
                        SqlTableNameSuffix = suffix
                    });
                }
            }
        }

        // Archives: symbols in the current master (active) that are NOT in the upload
        // and NOT involved in a rename resolution.
        foreach (var sym in currentSymbols)
        {
            if (sym.IsArchived) continue;
            if (uploadedSymbols.Contains(sym.Symbol)) continue;
            if (resolvedRenames.Contains(sym.Symbol)) continue;

            archives.Add(new DiffArchiveEntry
            {
                Symbol = sym.Symbol,
                CompanyName = sym.CompanyName,
                Isin = sym.Isin
            });
        }

        return new DiffPreview
        {
            Adds = adds,
            Archives = archives,
            IndustryChanges = industryChanges,
            Skipped = skipped,
            RenameCandidates = renameCandidates,
            TotalRows = rows.Count,
            AcceptedRows = rows.Count - skipped.Count,
            FileHash = fileHash
        };
    }

    /// <summary>
    /// Commits a CSV upload: snapshot, apply changes, record result.
    /// REQ-UNIV-011..020.
    /// </summary>
    public async Task<CommitResult> CommitAsync(
        CommitRequest request, CancellationToken ct = default)
    {
        // Check archive-confirm threshold. REQ-UNIV-018.
        var activeCount = await _symbolRepo.CountAsync(archived: false, ct);
        var archiveThresholdPct = 5; // default, would come from sys_config in full impl.
        var maxArchives = (int)Math.Ceiling(activeCount * archiveThresholdPct / 100.0);

        if (request.Archives.Count > maxArchives)
        {
            var expectedPhrase = $"CONFIRM_ARCHIVE_{request.Archives.Count}";
            if (string.IsNullOrWhiteSpace(request.ConfirmationPhrase) ||
                request.ConfirmationPhrase != expectedPhrase)
            {
                return new CommitResult
                {
                    Success = false,
                    Error = $"Archive count {request.Archives.Count} exceeds threshold ({maxArchives}). " +
                            $"Typed confirmation phrase '{expectedPhrase}' and justification are required."
                };
            }

            if (string.IsNullOrWhiteSpace(request.Justification))
            {
                return new CommitResult
                {
                    Success = false,
                    Error = "Written justification is required when archive count exceeds threshold."
                };
            }
        }

        // Build the rename resolution map: symbols that are being renamed (approved).
        var approvedRenames = request.RenameResolutions
            .Where(r => r.Resolution == "approved")
            .ToDictionary(r => r.OldSymbol, StringComparer.OrdinalIgnoreCase);

        // Rejected renames: old symbol will be archived (already in archives list),
        // new symbol needs a fresh record.
        var rejectedRenames = request.RenameResolutions
            .Where(r => r.Resolution == "rejected")
            .ToList();

        // Take a pre-commit snapshot. REQ-UNIV-019.
        var allSymbols = await _symbolRepo.GetAllAsync(archived: null, ct);
        var snapshot = CaptureSnapshot(allSymbols);

        var now = DateTime.UtcNow;
        var newSymbolsAdded = new List<string>();
        var archivedSymbolsList = new List<string>();

        try
        {
            // Step 1: Process approved renames — update symbol in place, preserve suffix.
            foreach (var (oldSymbol, rename) in approvedRenames)
            {
                var existing = allSymbols.FirstOrDefault(s =>
                    string.Equals(s.Symbol, oldSymbol, StringComparison.OrdinalIgnoreCase));
                if (existing is null) continue;

                await _symbolRepo.UpdateMetadataAsync(
                    existing.Id,
                    isArchived: false,
                    ct: ct);
                // For a rename, we also need to change the symbol string itself.
                // The current repository doesn't support this, so we do it via direct update.
                // We need to extend the repository. Let's call a helper.
                await RenameSymbolInPlaceAsync(existing.Id, rename.NewSymbol, ct);
            }

            // Step 2: Archive symbols that are absent from upload (and not rename-resolved).
            foreach (var archive in request.Archives)
            {
                var existing = allSymbols.FirstOrDefault(s =>
                    string.Equals(s.Symbol, archive.Symbol, StringComparison.OrdinalIgnoreCase));
                if (existing is null) continue;

                await _symbolRepo.UpdateMetadataAsync(
                    existing.Id,
                    isArchived: true,
                    ct: ct);
                archivedSymbolsList.Add(archive.Symbol);
            }

            // Step 3: Add new symbols — including net-new adds and rejected-rename new symbols.
            var allAdds = request.Adds.ToList();
            foreach (var rejected in rejectedRenames)
            {
                // Check if the client already included this symbol.
                if (allAdds.Any(a => string.Equals(a.Symbol, rejected.NewSymbol,
                        StringComparison.OrdinalIgnoreCase)))
                    continue;

                var suffix = SqlTableNameSuffixGenerator.Generate(rejected.NewSymbol);
                allAdds.Add(new DiffAddEntry
                {
                    Symbol = rejected.NewSymbol,
                    CompanyName = rejected.CompanyName,
                    Industry = rejected.Industry,
                    Isin = rejected.Isin,
                    SqlTableNameSuffix = suffix
                });
            }

            foreach (var add in allAdds)
            {
                // Check suffix collision.
                var collision = await _symbolRepo.FindBySuffixAsync(add.SqlTableNameSuffix, ct);
                if (collision is not null)
                {
                    return new CommitResult
                    {
                        Success = false,
                        Error = $"sql_table_name_suffix '{add.SqlTableNameSuffix}' already in use " +
                                $"by symbol '{collision.Symbol}'. Cannot add '{add.Symbol}'."
                    };
                }

                var doc = new SymbolMasterDocument
                {
                    Symbol = add.Symbol.ToUpperInvariant(),
                    CompanyName = add.CompanyName,
                    Industry = add.Industry,
                    Series = "EQ",
                    Isin = add.Isin,
                    SqlTableNameSuffix = add.SqlTableNameSuffix,
                    LotSize = 1,
                    IsArchived = false,
                    ScanExcluded = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _symbolRepo.CreateAsync(doc, ct);
                newSymbolsAdded.Add(add.Symbol);
            }

            // Step 4: Apply industry classification changes. REQ-UNIV-002a.
            foreach (var change in request.IndustryChanges)
            {
                var existing = allSymbols.FirstOrDefault(s =>
                    string.Equals(s.Symbol, change.Symbol, StringComparison.OrdinalIgnoreCase));
                if (existing is null) continue;

                await _symbolRepo.UpdateMetadataAsync(
                    existing.Id,
                    industry: change.NewIndustry,
                    ct: ct);
            }

            // Step 5: Record the upload result with snapshot.
            var uploadDoc = new UniverseUploadResultDocument
            {
                UploadedBy = request.AdminUserId,
                UploadedAt = now,
                FileHash = request.FileHash,
                TotalRows = request.TotalRows,
                RowsAccepted = request.TotalRows - request.Skipped.Count,
                RowsArchived = archivedSymbolsList.Count,
                RowsExcluded = request.Skipped.Count,
                NewSymbols = newSymbolsAdded,
                ArchivedSymbols = archivedSymbolsList,
                IndustryChanges = request.IndustryChanges.Select(c => new IndustryChangeEntry
                {
                    Symbol = c.Symbol,
                    PreviousIndustry = c.PreviousIndustry,
                    NewIndustry = c.NewIndustry
                }).ToList(),
                SkippedRows = request.Skipped.Select(s => new SkippedRowEntry
                {
                    Symbol = s.Symbol,
                    CompanyName = s.CompanyName,
                    Series = s.Series,
                    Reason = $"Series is '{s.Series}', not 'EQ'"
                }).ToList(),
                RenameResolutions = request.RenameResolutions.Select(r => new RenameResolutionEntry
                {
                    OldSymbol = r.OldSymbol,
                    NewSymbol = r.NewSymbol,
                    Isin = r.Isin,
                    Resolution = r.Resolution
                }).ToList(),
                HdsTriggered = newSymbolsAdded.Count > 0,
                RolledBack = false,
                Snapshot = snapshot
            };

            await _uploadRepo.CreateAsync(uploadDoc, ct);

            _logger.LogInformation(
                "Universe sync committed by {Admin}: {NewCount} added, {ArchiveCount} archived, {RenameCount} renames, {ChangeCount} industry changes, HDS triggered: {Hds}",
                request.AdminUserId, newSymbolsAdded.Count, archivedSymbolsList.Count,
                approvedRenames.Count, request.IndustryChanges.Count, newSymbolsAdded.Count > 0);

            return new CommitResult
            {
                Success = true,
                UploadId = uploadDoc.Id,
                NewSymbols = newSymbolsAdded,
                ArchivedSymbols = archivedSymbolsList
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Universe sync commit failed for admin {Admin}", request.AdminUserId);
            return new CommitResult
            {
                Success = false,
                Error = $"Commit failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Rolls back a previous upload by restoring its pre-commit snapshot.
    /// REQ-UNIV-019.
    /// </summary>
    public async Task<RollbackResult> RollbackAsync(
        ObjectId uploadId, string adminUserId, int retentionDays, CancellationToken ct = default)
    {
        var upload = await _uploadRepo.FindByIdAsync(uploadId, ct);
        if (upload is null)
            return new RollbackResult { Success = false, Error = "Upload record not found." };

        if (upload.RolledBack)
            return new RollbackResult { Success = false, Error = "This upload has already been rolled back." };

        // Check retention window.
        var age = DateTime.UtcNow - upload.UploadedAt;
        if (age.TotalDays > retentionDays)
            return new RollbackResult
            {
                Success = false,
                Error = $"Upload is outside the {retentionDays}-day rollback retention window " +
                        $"({age.TotalDays:F1} days old)."
            };

        // Check it's the most recent upload (sequential rollback only).
        var latest = await _uploadRepo.GetLatestAsync(ct);
        if (latest is null || latest.Id != uploadId)
            return new RollbackResult
            {
                Success = false,
                Error = "Only the most recent upload can be rolled back. " +
                        "Roll back sequentially from the most recent."
            };

        var snapshot = upload.Snapshot;
        if (snapshot is null)
            return new RollbackResult { Success = false, Error = "No snapshot available for this upload." };

        // Get all current symbols.
        var currentSymbols = await _symbolRepo.GetAllAsync(archived: null, ct);

        try
        {
            // Step 1: First, revert all approved renames so symbols carry their original names
            // before we compare against the snapshot. This ensures the subsequent
            // archive/reactivate logic works correctly (REQ-UNIV-019).
            var approvedReverts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rename in upload.RenameResolutions)
            {
                if (rename.Resolution != "approved") continue;
                var renamedDoc = currentSymbols.FirstOrDefault(s =>
                    string.Equals(s.Symbol, rename.NewSymbol, StringComparison.OrdinalIgnoreCase));
                if (renamedDoc is null) continue;

                await RenameSymbolInPlaceAsync(renamedDoc.Id, rename.OldSymbol, ct);
                approvedReverts[rename.NewSymbol] = rename.OldSymbol;
            }

            // Refresh symbol list after renames.
            currentSymbols = await _symbolRepo.GetAllAsync(archived: null, ct);
            var snapshotBySymbol = snapshot.Symbols
                .ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);

            // Step 2: Match each current symbol against the snapshot and restore state.
            foreach (var current in currentSymbols)
            {
                if (snapshotBySymbol.TryGetValue(current.Symbol, out var snapEntry))
                {
                    // Symbol existed at snapshot time — restore its state.
                    var needsIndustryUpdate = !string.Equals(
                        current.Industry, snapEntry.Industry, StringComparison.Ordinal);
                    var needsArchiveUpdate = current.IsArchived != snapEntry.IsArchived;

                    if (needsIndustryUpdate || needsArchiveUpdate)
                    {
                        await _symbolRepo.UpdateMetadataAsync(
                            current.Id,
                            industry: needsIndustryUpdate ? snapEntry.Industry : null,
                            isArchived: needsArchiveUpdate ? snapEntry.IsArchived : null,
                            ct: ct);
                    }
                }
                else
                {
                    // Symbol does not exist in snapshot — it was added by this upload. Remove it.
                    // Soft-remove by archiving to preserve referential integrity (REQ-UNIV-019).
                    if (!current.IsArchived)
                    {
                        await _symbolRepo.UpdateMetadataAsync(
                            current.Id,
                            isArchived: true,
                            scanExcluded: true,
                            ct: ct);
                    }
                }
            }

            // Step 3: Reactivate symbols that were archived by this upload.
            var sessionSymbols = currentSymbols.ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);
            foreach (var uploadArchive in upload.ArchivedSymbols)
            {
                // Account for approved renames that were reverted above —
                // the symbol is now in sessionSymbols by its old name.
                var archiveSymbol = approvedReverts
                    .FirstOrDefault(kvp => string.Equals(kvp.Value, uploadArchive,
                        StringComparison.OrdinalIgnoreCase)).Value ?? uploadArchive;

                if (sessionSymbols.TryGetValue(archiveSymbol, out var archived))
                {
                    if (archived.IsArchived)
                    {
                        await _symbolRepo.UpdateMetadataAsync(
                            archived.Id,
                            isArchived: false,
                            ct: ct);
                    }
                }
            }

            // Step 4: Restore industry for symbols that were changed.
            foreach (var change in upload.IndustryChanges)
            {
                if (sessionSymbols.TryGetValue(change.Symbol, out var changedSym))
                {
                    await _symbolRepo.UpdateMetadataAsync(
                        changedSym.Id,
                        industry: change.PreviousIndustry,
                        ct: ct);
                }
            }

            // Mark the upload as rolled back.
            await _uploadRepo.MarkRolledBackAsync(uploadId, adminUserId, DateTime.UtcNow, ct);

            _logger.LogInformation(
                "Universe sync upload {UploadId} rolled back by {Admin}",
                uploadId, adminUserId);

            return new RollbackResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed for upload {UploadId}", uploadId);
            return new RollbackResult
            {
                Success = false,
                Error = $"Rollback failed: {ex.Message}"
            };
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private static List<CsvRow> ParseCsv(string csvContent)
    {
        var rows = new List<CsvRow>();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
            throw new InvalidDataException("CSV must contain a header row and at least one data row.");

        // Parse header — find column indices.
        var header = lines[0].Split(',').Select(h => h.Trim('"', ' ')).ToArray();
        var companyNameIdx = FindColumnIndex(header, "Company Name");
        var industryIdx = FindColumnIndex(header, "Industry");
        var symbolIdx = FindColumnIndex(header, "Symbol");
        var seriesIdx = FindColumnIndex(header, "Series");
        var isinIdx = FindColumnIndex(header, "ISIN Code");

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var fields = SplitCsvLine(lines[i]);
            if (fields.Length < 5) continue;

            rows.Add(new CsvRow(
                CompanyName: GetField(fields, companyNameIdx),
                Industry: GetField(fields, industryIdx),
                Symbol: GetField(fields, symbolIdx),
                Series: GetField(fields, seriesIdx),
                IsinCode: GetField(fields, isinIdx)));
        }

        return rows;
    }

    private static string[] SplitCsvLine(string line)
    {
        // Simple CSV split handling quoted fields.
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim().Trim('"'));
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString().Trim().Trim('"'));
        return fields.ToArray();
    }

    private static int FindColumnIndex(string[] header, string target)
    {
        for (var i = 0; i < header.Length; i++)
        {
            if (string.Equals(header[i], target, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new InvalidDataException($"Required column '{target}' not found in CSV header.");
    }

    private static string GetField(string[] fields, int idx)
    {
        return idx >= 0 && idx < fields.Length ? fields[idx].Trim() : "";
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static UniverseSnapshot CaptureSnapshot(List<SymbolMasterDocument> symbols)
    {
        return new UniverseSnapshot
        {
            Symbols = symbols.Select(s => new SymbolSnapshotEntry
            {
                Symbol = s.Symbol,
                CompanyName = s.CompanyName,
                Industry = s.Industry,
                Series = s.Series,
                Isin = s.Isin,
                SqlTableNameSuffix = s.SqlTableNameSuffix,
                LotSize = s.LotSize,
                IsArchived = s.IsArchived,
                ScanExcluded = s.ScanExcluded
            }).ToList()
        };
    }

    private async Task RenameSymbolInPlaceAsync(
        ObjectId id, string newSymbol, CancellationToken ct)
    {
        await _symbolRepo.RenameSymbolAsync(id, newSymbol, ct);
    }
}
