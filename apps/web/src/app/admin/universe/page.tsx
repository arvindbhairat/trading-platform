"use client";

// Admin universe sync page (P3-T5 / REQ-UNIV-011..020).
// CSV upload with diff preview, rename detection, archive-confirm threshold, rollback.

import { useEffect, useState, useCallback, useRef } from "react";
import { useRouter } from "next/navigation";
import {
  Shell,
  Card,
  Btn,
  Pill,
  TextInput,
  Field,
  adminNavItems,
  Icon,
} from "@/components/primitives";
import { getToken, apiFetch } from "@/lib/auth";

// ── Types ──────────────────────────────────────────────────────────────

interface CoverageData {
  unconfirmed_count: number;
  earliest_unconfirmed_date: string | null;
  unconfirmed_dates: string[];
  checked_from: string;
  checked_to: string;
}

interface SymbolEntry {
  symbol: string;
  company_name: string;
  industry: string;
  isin: string;
  is_archived: boolean;
  scan_excluded: boolean;
  sql_table_name_suffix: string;
}

interface UploadHistoryEntry {
  id: string;
  uploaded_by: string;
  uploaded_at: string;
  file_hash: string;
  total_rows: number;
  rows_accepted: number;
  rows_archived: number;
  rows_excluded: number;
  new_symbols: string[];
  archived_symbols: string[];
  hds_triggered: boolean;
  rolled_back: boolean;
  rolled_back_at: string | null;
  rolled_back_by: string | null;
}

interface DiffAdd {
  symbol: string;
  company_name: string;
  industry: string;
  isin: string;
  sql_table_name_suffix: string;
}

interface DiffArchive {
  symbol: string;
  company_name: string;
  isin: string;
}

interface DiffIndustryChange {
  symbol: string;
  previous_industry: string;
  new_industry: string;
}

interface DiffSkipped {
  symbol: string;
  company_name: string;
  series: string;
}

interface DiffRenameCandidate {
  old_symbol: string;
  new_symbol: string;
  isin: string;
}

interface DiffPreviewData {
  file_hash: string;
  total_rows: number;
  accepted_rows: number;
  adds: DiffAdd[];
  archives: DiffArchive[];
  industry_changes: DiffIndustryChange[];
  skipped: DiffSkipped[];
  rename_candidates: DiffRenameCandidate[];
}

interface RenameResolution {
  old_symbol: string;
  new_symbol: string;
  isin: string;
  company_name: string;
  industry: string;
  resolution: "approved" | "rejected";
}

type Step = "idle" | "preview" | "commit";
type Notification = { type: "success" | "error"; message: string };

// ── Page Component ─────────────────────────────────────────────────────

export default function AdminUniversePage() {
  const router = useRouter();

  // Symbol master list.
  const [symbols, setSymbols] = useState<SymbolEntry[]>([]);
  const [loadingSymbols, setLoadingSymbols] = useState(true);

  // Upload flow state.
  const [step, setStep] = useState<Step>("idle");
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<DiffPreviewData | null>(null);
  const [renameResolutions, setRenameResolutions] = useState<Map<string, RenameResolution>>(new Map());
  const [confirmationPhrase, setConfirmationPhrase] = useState("");
  const [justification, setJustification] = useState("");
  const [showConfirmDialog, setShowConfirmDialog] = useState(false);
  const [committing, setCommitting] = useState(false);
  const [notification, setNotification] = useState<Notification | null>(null);
  const [showUploadForm, setShowUploadForm] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Upload history.
  const [uploads, setUploads] = useState<UploadHistoryEntry[]>([]);
  const [loadingUploads, setLoadingUploads] = useState(true);
  const [rollbacking, setRollbacking] = useState<string | null>(null);

  // Age indicator.
  const [staleWarning, setStaleWarning] = useState<string | null>(null);

  // ── Data fetching ────────────────────────────────────────────────────

  const fetchSymbols = useCallback(async () => {
    try {
      const token = getToken();
      if (!token) {
        router.replace("/login");
        return;
      }
      const res = await apiFetch("/api/v1/universe/symbols?archived=false");
      if (!res.ok) {
        if (res.status === 401 || res.status === 403) router.replace("/login");
        return;
      }
      const data: SymbolEntry[] = await res.json();
      setSymbols(data);
    } catch {
      // ignore
    } finally {
      setLoadingSymbols(false);
    }
  }, [router]);

  const fetchUploads = useCallback(async () => {
    setLoadingUploads(true);
    try {
      const token = getToken();
      if (!token) return;
      const res = await apiFetch("/api/v1/admin/universe/uploads");
      if (!res.ok) return;
      const data = await res.json();
      setUploads(data.uploads ?? []);

      // Check age indicator. REQ-UNIV-016.
      if (data.uploads && data.uploads.length > 0) {
        const latest = data.uploads[0] as UploadHistoryEntry;
        const ageDays = (Date.now() - new Date(latest.uploaded_at).getTime()) / 86400000;
        if (ageDays > 180) {
          setStaleWarning(
            `Last universe upload was ${Math.floor(ageDays)} days ago (${new Date(latest.uploaded_at).toLocaleDateString("en-IN")}). Consider refreshing against the latest NSE Nifty 500 composition.`
          );
        } else {
          setStaleWarning(null);
        }
      } else {
        setStaleWarning("No universe uploads recorded. Upload a Nifty 500 CSV to initialise the universe.");
      }
    } catch {
      // ignore
    } finally {
      setLoadingUploads(false);
    }
  }, []);

  useEffect(() => {
    fetchSymbols();
    fetchUploads();
  }, [fetchSymbols, fetchUploads]);

  // ── Upload / Preview ────────────────────────────────────────────────

  function handleFileSelect(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    setSelectedFile(file);
    setNotification(null);
    setStep("idle");
    setPreview(null);
    setShowConfirmDialog(false);
    setConfirmationPhrase("");
    setJustification("");
    setRenameResolutions(new Map());
  }

  async function handleUpload() {
    if (!selectedFile) {
      setNotification({ type: "error", message: "Please select a CSV file." });
      return;
    }

    setNotification(null);
    setPreview(null);
    setStep("idle");

    try {
      const token = getToken();
      if (!token) { router.replace("/login"); return; }

      const formData = new FormData();
      formData.append("file", selectedFile);

      const res = await apiFetch("/api/v1/admin/universe/upload/preview", {
        method: "POST",
        body: formData,
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: body.error ?? "Preview failed" });
        return;
      }

      const data: DiffPreviewData = await res.json();
      setPreview(data);
      setStep("preview");

      // Initialise rename resolutions: default each candidate to "approved".
      const resolutions = new Map<string, RenameResolution>();
      for (const r of data.rename_candidates) {
        resolutions.set(r.old_symbol, {
          old_symbol: r.old_symbol,
          new_symbol: r.new_symbol,
          isin: r.isin,
          company_name: r.company_name,
          industry: r.industry,
          resolution: "approved",
        });
      }
      setRenameResolutions(resolutions);
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Upload failed",
      });
    }
  }

  // ── Rename resolution ───────────────────────────────────────────────

  function setRenameResolution(oldSymbol: string, resolution: "approved" | "rejected") {
    setRenameResolutions((prev) => {
      const next = new Map(prev);
      const existing = next.get(oldSymbol);
      if (existing) {
        next.set(oldSymbol, { ...existing, resolution });
      }
      return next;
    });
  }

  // ── Archive threshold check ─────────────────────────────────────────

  function getArchiveThresholdMet(): { met: boolean; pct: number; maxArchives: number } {
    const pct = 5; // Default threshold; sys_config would be read server-side.
    const maxArchives = Math.ceil(symbols.length * pct / 100);
    const archives = preview?.archives.length ?? 0;
    return { met: archives > maxArchives, pct, maxArchives };
  }

  // ── Commit ──────────────────────────────────────────────────────────

  async function handleCommit() {
    if (!preview) return;

    const threshold = getArchiveThresholdMet();
    if (threshold.met) {
      const expectedPhrase = `CONFIRM_ARCHIVE_${preview.archives.length}`;
      if (confirmationPhrase !== expectedPhrase) {
        setNotification({
          type: "error",
          message: `Archive count ${preview.archives.length} exceeds threshold. Please type "${expectedPhrase}" to confirm.`,
        });
        return;
      }
      if (!justification.trim()) {
        setNotification({
          type: "error",
          message: "A written justification is required when the archive threshold is exceeded.",
        });
        return;
      }
    }

    setCommitting(true);
    setNotification(null);

    try {
      const token = getToken();
      if (!token) { router.replace("/login"); return; }

      const resolutions = Array.from(renameResolutions.values());
      const resolvedSymbols = new Set(resolutions.map((r) => r.old_symbol));

      // Archives: only include symbols NOT resolved as approved rename.
      // For rejected renames: the old symbol remains in (or is added to) the archives list.
      // The backend automatically creates a fresh record for the new symbol.
      const archives = preview.archives.filter(
        (a) => !resolvedSymbols.has(a.symbol)
      );
      // Ensure rejected-rename old symbols are in archives.
      for (const r of resolutions) {
        if (r.resolution === "rejected") {
          const existing = archives.find((a) => a.symbol === r.old_symbol);
          if (!existing) {
            archives.push({
              symbol: r.old_symbol,
              company_name: "",
              isin: r.isin,
            });
          }
        }
      }

      const body = {
        file_hash: preview.file_hash,
        total_rows: preview.total_rows,
        adds: preview.adds,
        archives,
        industry_changes: preview.industry_changes,
        skipped: preview.skipped,
        rename_resolutions: resolutions,
        confirmation_phrase: threshold.met ? confirmationPhrase : null,
        justification: threshold.met ? justification.trim() : null,
      };

      const res = await apiFetch("/api/v1/admin/universe/upload/commit", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: err.error ?? "Commit failed" });
        setCommitting(false);
        return;
      }

      setNotification({ type: "success", message: "Universe sync committed successfully." });
      setStep("idle");
      setPreview(null);
      setSelectedFile(null);
      setShowUploadForm(false);
      fetchSymbols();
      fetchUploads();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Commit failed",
      });
    } finally {
      setCommitting(false);
    }
  }

  // ── Rollback ────────────────────────────────────────────────────────

  async function handleRollback(uploadId: string) {
    if (!confirm("Rolling back will revert the symbol master to its state before this upload. Continue?")) return;

    setRollbacking(uploadId);
    setNotification(null);

    try {
      const token = getToken();
      if (!token) { router.replace("/login"); return; }

      const res = await apiFetch(`/api/v1/admin/universe/upload/${uploadId}/rollback`, {
        method: "POST",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        setNotification({ type: "error", message: err.error ?? "Rollback failed" });
        setRollbacking(null);
        return;
      }

      setNotification({ type: "success", message: "Upload rolled back successfully." });
      fetchSymbols();
      fetchUploads();
    } catch (err) {
      setNotification({
        type: "error",
        message: err instanceof Error ? err.message : "Rollback failed",
      });
    } finally {
      setRollbacking(null);
    }
  }

  // ── Helpers ─────────────────────────────────────────────────────────

  function formatDate(dateStr: string): string {
    if (!dateStr) return "";
    const d = new Date(dateStr);
    return d.toLocaleDateString("en-IN", {
      weekday: "short",
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      timeZone: "Asia/Kolkata",
    });
  }

  function formatAge(dateStr: string): string {
    const days = Math.floor((Date.now() - new Date(dateStr).getTime()) / 86400000);
    if (days < 1) return "Today";
    if (days === 1) return "1 day ago";
    if (days < 30) return `${days} days ago`;
    const months = Math.floor(days / 30);
    return `${months} month${months > 1 ? "s" : ""} ago`;
  }

  // ── Render ──────────────────────────────────────────────────────────

  const threshold = preview ? getArchiveThresholdMet() : null;
  const activeCount = symbols.filter((s) => !s.is_archived).length;
  const archivedCount = symbols.filter((s) => s.is_archived).length;

  return (
    <Shell current="universe" navItems={adminNavItems}>
      <div style={{ padding: "var(--s-8) var(--s-10)", maxWidth: "1200px" }}>
        {/* Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "var(--s-2)",
          }}
        >
          <h1>Universe management</h1>
          <div style={{ display: "flex", gap: "var(--s-3)" }}>
            <Btn
              variant="ghost"
              icon="refresh"
              size="sm"
              onClick={() => { fetchSymbols(); fetchUploads(); }}
            >
              Refresh
            </Btn>
            <Btn
              variant="primary"
              icon="upload"
              size="sm"
              onClick={() => setShowUploadForm(!showUploadForm)}
            >
              {showUploadForm ? "Cancel upload" : "Upload CSV"}
            </Btn>
          </div>
        </div>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Manage the Nifty 500 universe. Upload a new CSV to sync with the latest
          NSE composition.
        </p>

        {/* Notification banner */}
        {notification && (
          <Card
            accent={notification.type === "success" ? "up" : "warn"}
            style={{
              padding: "var(--s-4) var(--s-6)",
              marginBottom: "var(--s-6)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <span className="t-body-sm">{notification.message}</span>
            <button
              onClick={() => setNotification(null)}
              style={{
                background: "none",
                border: "none",
                color: "var(--t-2)",
                cursor: "pointer",
                padding: "var(--s-1)",
              }}
              aria-label="Dismiss"
            >
              <Icon name="x" size={16} />
            </button>
          </Card>
        )}

        {/* Stale age warning — REQ-UNIV-016 */}
        {staleWarning && (
          <Card
            accent="warn"
            style={{
              padding: "var(--s-4) var(--s-6)",
              marginBottom: "var(--s-6)",
              display: "flex",
              alignItems: "center",
              gap: "var(--s-3)",
            }}
          >
            <Icon name="alert-triangle" size={18} />
            <span className="t-body-sm">{staleWarning}</span>
          </Card>
        )}

        {/* ── CSV Upload Form ──────────────────────────────────────── */}
        {showUploadForm && (
          <Card
            accent="brand"
            style={{
              padding: "var(--s-6)",
              marginBottom: "var(--s-6)",
            }}
          >
            <h3 style={{ marginBottom: "var(--s-4)" }}>
              Upload Nifty 500 CSV
            </h3>
            <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
              Required columns: <code>Company Name</code>, <code>Industry</code>,{" "}
              <code>Symbol</code>, <code>Series</code>, <code>ISIN Code</code>.
              Only rows with <code>Series = EQ</code> will be imported.
            </p>

            <div style={{ marginBottom: "var(--s-4)" }}>
              <input
                ref={fileInputRef}
                type="file"
                accept=".csv"
                onChange={handleFileSelect}
                style={{
                  display: "block",
                  padding: "var(--s-2)",
                  border: "1px solid var(--border-1)",
                  borderRadius: "var(--r-2)",
                  background: "var(--bg-1)",
                  color: "var(--t-0)",
                  fontSize: "var(--fs-sm)",
                  width: "100%",
                  maxWidth: "500px",
                }}
              />
            </div>

            {selectedFile && (
              <div style={{ marginBottom: "var(--s-4)" }}>
                <span className="t-body-sm" style={{ color: "var(--t-2)" }}>
                  Selected: {selectedFile.name} ({(selectedFile.size / 1024).toFixed(1)} KB)
                </span>
              </div>
            )}

            <Btn
              variant="primary"
              size="sm"
              onClick={handleUpload}
              disabled={!selectedFile}
            >
              Preview diff
            </Btn>
          </Card>
        )}

        {/* ── Diff Preview ─────────────────────────────────────────── */}
        {preview && step === "preview" && (
          <Card style={{ marginBottom: "var(--s-6)" }}>
            <div
              style={{
                padding: "var(--s-4) var(--s-5)",
                borderBottom: "1px solid var(--border-1)",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                }}
              >
                <h2 style={{ margin: 0 }}>Diff preview</h2>
                <div style={{ display: "flex", gap: "var(--s-2)" }}>
                  <Pill tone="info">{preview.total_rows} total rows</Pill>
                  <Pill tone="up">{preview.accepted_rows} accepted</Pill>
                  {preview.archives.length > 0 && (
                    <Pill tone="warn">{preview.archives.length} to archive</Pill>
                  )}
                  {preview.adds.length > 0 && (
                    <Pill tone="brand">{preview.adds.length} to add</Pill>
                  )}
                  {preview.rename_candidates.length > 0 && (
                    <Pill tone="info">{preview.rename_candidates.length} rename candidates</Pill>
                  )}
                </div>
              </div>
            </div>

            <div style={{ padding: "var(--s-5)" }}>
              {/* Rename candidates — REQ-UNIV-020 */}
              {preview.rename_candidates.length > 0 && (
                <div style={{ marginBottom: "var(--s-6)" }}>
                  <h3 style={{ marginBottom: "var(--s-3)" }}>
                    Rename candidates ({preview.rename_candidates.length})
                  </h3>
                  <div style={{ overflowX: "auto" }}>
                    <table
                      style={{
                        width: "100%",
                        borderCollapse: "collapse",
                        fontSize: "var(--fs-sm)",
                      }}
                    >
                      <thead>
                        <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                          <th style={{ padding: "var(--s-2) var(--s-3)" }}>Old symbol</th>
                          <th style={{ padding: "var(--s-2) var(--s-3)" }}>New symbol</th>
                          <th style={{ padding: "var(--s-2) var(--s-3)" }}>ISIN</th>
                          <th style={{ padding: "var(--s-2) var(--s-3)" }}>Resolution</th>
                        </tr>
                      </thead>
                      <tbody>
                        {preview.rename_candidates.map((r) => (
                          <tr
                            key={r.old_symbol}
                            style={{ borderBottom: "1px solid var(--border-1)" }}
                          >
                            <td
                              style={{
                                padding: "var(--s-2) var(--s-3)",
                                fontFamily: "var(--ff-mono)",
                              }}
                            >
                              {r.old_symbol}
                            </td>
                            <td
                              style={{
                                padding: "var(--s-2) var(--s-3)",
                                fontFamily: "var(--ff-mono)",
                              }}
                            >
                              {r.new_symbol}
                            </td>
                            <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                              <code>{r.isin}</code>
                            </td>
                            <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                              <div style={{ display: "flex", gap: "var(--s-2)" }}>
                                <Btn
                                  size="sm"
                                  variant={
                                    renameResolutions.get(r.old_symbol)?.resolution === "approved"
                                      ? "primary"
                                      : "ghost"
                                  }
                                  onClick={() => setRenameResolution(r.old_symbol, "approved")}
                                >
                                  Approve rename
                                </Btn>
                                <Btn
                                  size="sm"
                                  variant={
                                    renameResolutions.get(r.old_symbol)?.resolution === "rejected"
                                      ? "primary"
                                      : "ghost"
                                  }
                                  onClick={() => setRenameResolution(r.old_symbol, "rejected")}
                                >
                                  Archive & add new
                                </Btn>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <p className="t-body-sm" style={{ color: "var(--t-3)", marginTop: "var(--s-2)" }}>
                    <strong>Approve rename</strong>: updates the symbol in-place, preserves SQL tables and all
                    historical references. <strong>Archive &amp; add new</strong>: treats as separate archive of old and
                    fresh addition of new symbol with new SQL tables.
                  </p>
                </div>
              )}

              {/* Adds — REQ-UNIV-011 */}
              {preview.adds.length > 0 && (
                <div style={{ marginBottom: "var(--s-4)" }}>
                  <h3 style={{ marginBottom: "var(--s-2)" }}>
                    New symbols ({preview.adds.length})
                  </h3>
                  <div
                    style={{
                      display: "flex",
                      flexWrap: "wrap",
                      gap: "var(--s-1)",
                      fontFamily: "var(--ff-mono)",
                      fontSize: "var(--fs-sm)",
                    }}
                  >
                    {preview.adds.map((a) => (
                      <Pill key={a.symbol} tone="brand">
                        {a.symbol}
                      </Pill>
                    ))}
                  </div>
                </div>
              )}

              {/* Archives — REQ-UNIV-012 */}
              {preview.archives.length > 0 && (
                <div style={{ marginBottom: "var(--s-4)" }}>
                  <h3 style={{ marginBottom: "var(--s-2)" }}>
                    Symbols to archive ({preview.archives.length})
                  </h3>
                  <div
                    style={{
                      display: "flex",
                      flexWrap: "wrap",
                      gap: "var(--s-1)",
                      fontFamily: "var(--ff-mono)",
                      fontSize: "var(--fs-sm)",
                    }}
                  >
                    {preview.archives.map((a) => (
                      <Pill key={a.symbol} tone="warn">
                        {a.symbol}
                      </Pill>
                    ))}
                  </div>
                </div>
              )}

              {/* Industry changes — REQ-UNIV-002a */}
              {preview.industry_changes.length > 0 && (
                <div style={{ marginBottom: "var(--s-4)" }}>
                  <h3 style={{ marginBottom: "var(--s-2)" }}>
                    Industry reclassifications ({preview.industry_changes.length})
                  </h3>
                  <div style={{ overflowX: "auto" }}>
                    <table
                      style={{
                        width: "100%",
                        borderCollapse: "collapse",
                        fontSize: "var(--fs-sm)",
                      }}
                    >
                      <thead>
                        <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                          <th style={{ padding: "var(--s-2) var(--s-3)" }}>Symbol</th>
                          <th style={{ padding: "var(--s-2) var(--s-3)" }}>Previous industry</th>
                          <th style={{ padding: "var(--s-2) var(--s-3)" }}>New industry</th>
                        </tr>
                      </thead>
                      <tbody>
                        {preview.industry_changes.map((c) => (
                          <tr
                            key={c.symbol}
                            style={{ borderBottom: "1px solid var(--border-1)" }}
                          >
                            <td
                              style={{
                                padding: "var(--s-2) var(--s-3)",
                                fontFamily: "var(--ff-mono)",
                              }}
                            >
                              {c.symbol}
                            </td>
                            <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                              {c.previous_industry}
                            </td>
                            <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                              {c.new_industry}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* Skipped rows — REQ-UNIV-013 */}
              {preview.skipped.length > 0 && (
                <div style={{ marginBottom: "var(--s-4)" }}>
                  <h3 style={{ marginBottom: "var(--s-2)" }}>
                    Skipped rows ({preview.skipped.length})
                  </h3>
                  <div
                    style={{
                      display: "flex",
                      flexWrap: "wrap",
                      gap: "var(--s-1)",
                      fontFamily: "var(--ff-mono)",
                      fontSize: "var(--fs-sm)",
                    }}
                  >
                    {preview.skipped.map((s) => (
                      <Pill key={s.symbol} tone="info">
                        {s.symbol} (Series: {s.series})
                      </Pill>
                    ))}
                  </div>
                </div>
              )}

              {/* Archive threshold confirmation — REQ-UNIV-018 */}
              {threshold?.met && (
                <div
                  style={{
                    marginTop: "var(--s-6)",
                    padding: "var(--s-4)",
                    background: "var(--bg-1)",
                    border: "1px solid var(--border-1)",
                    borderRadius: "var(--r-2)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "var(--s-3)",
                      marginBottom: "var(--s-3)",
                    }}
                  >
                    <Icon name="alert-triangle" size={20} />
                    <h3 style={{ margin: 0 }}>
                      Archive threshold exceeded
                    </h3>
                    <Pill tone="warn">Requires confirmation</Pill>
                  </div>
                  <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
                    This upload would archive {preview.archives.length} symbols, exceeding the{" "}
                    {threshold.pct}% threshold ({threshold.maxArchives} symbols). To proceed,
                    type the confirmation phrase below and provide a written justification.
                  </p>
                  <Field label={`Type "${threshold.met}" to confirm`}>
                    <TextInput
                      type="text"
                      value={confirmationPhrase}
                      onChange={(e) => setConfirmationPhrase(e.target.value)}
                      placeholder={`CONFIRM_ARCHIVE_${preview.archives.length}`}
                      style={{ maxWidth: "400px" }}
                    />
                  </Field>
                  <Field label="Justification (required)">
                    <textarea
                      value={justification}
                      onChange={(e) => setJustification(e.target.value)}
                      placeholder="Explain why these symbols are being removed from the universe..."
                      rows={3}
                      style={{
                        display: "block",
                        width: "100%",
                        maxWidth: "600px",
                        padding: "var(--s-2)",
                        border: "1px solid var(--border-1)",
                        borderRadius: "var(--r-2)",
                        background: "var(--bg-1)",
                        color: "var(--t-0)",
                        fontSize: "var(--fs-sm)",
                        fontFamily: "inherit",
                        resize: "vertical",
                      }}
                    />
                  </Field>
                </div>
              )}

              {/* Commit button */}
              <div
                style={{
                  marginTop: "var(--s-6)",
                  borderTop: "1px solid var(--border-1)",
                  paddingTop: "var(--s-4)",
                  display: "flex",
                  gap: "var(--s-3)",
                }}
              >
                <Btn
                  variant="primary"
                  size="sm"
                  onClick={() => setShowConfirmDialog(true)}
                  disabled={committing}
                >
                  {committing ? "Committing…" : "Commit upload"}
                </Btn>
                <Btn
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setStep("idle");
                    setPreview(null);
                    setShowUploadForm(false);
                  }}
                >
                  Cancel
                </Btn>
              </div>
            </div>
          </Card>
        )}

        {/* ── Commit confirmation dialog ──────────────────────────── */}
        {showConfirmDialog && preview && (
          <Card
            accent="brand"
            style={{
              marginBottom: "var(--s-6)",
              padding: "var(--s-6)",
              maxWidth: "600px",
            }}
          >
            <h3 style={{ marginBottom: "var(--s-3)" }}>Confirm universe sync</h3>
            <p className="t-body-sm" style={{ marginBottom: "var(--s-4)" }}>
              This action will update the symbol master with the changes shown in the diff
              preview. A pre-upload snapshot will be retained for rollback within the
              configured retention window.
            </p>
            <div style={{ display: "flex", gap: "var(--s-3)" }}>
              <Btn
                variant="primary"
                size="sm"
                onClick={handleCommit}
                disabled={committing}
              >
                {committing ? "Committing…" : "Confirm commit"}
              </Btn>
              <Btn
                variant="ghost"
                size="sm"
                onClick={() => setShowConfirmDialog(false)}
              >
                Cancel
              </Btn>
            </div>
          </Card>
        )}

        {/* ── Symbol Master Summary ────────────────────────────────── */}
        <Card style={{ marginBottom: "var(--s-6)" }}>
          <div
            style={{
              padding: "var(--s-4) var(--s-5)",
              borderBottom: "1px solid var(--border-1)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <h2 style={{ margin: 0 }}>
              Symbol master ({activeCount} active
              {archivedCount > 0 ? `, ${archivedCount} archived` : ""})
            </h2>
          </div>
          <div style={{ padding: "var(--s-5)" }}>
            {loadingSymbols ? (
              <p className="t-body-sm" style={{ color: "var(--t-3)" }}>
                Loading symbols…
              </p>
            ) : (
              <div style={{ overflowX: "auto", maxHeight: "400px", overflowY: "auto" }}>
                <table
                  style={{
                    width: "100%",
                    borderCollapse: "collapse",
                    fontSize: "var(--fs-sm)",
                  }}
                >
                  <thead>
                    <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Symbol</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Company</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Industry</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Status</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Suffix</th>
                    </tr>
                  </thead>
                  <tbody>
                    {symbols.map((s) => (
                      <tr
                        key={s.symbol}
                        style={{
                          borderBottom: "1px solid var(--border-1)",
                          opacity: s.is_archived ? 0.6 : 1,
                        }}
                      >
                        <td
                          style={{
                            padding: "var(--s-2) var(--s-3)",
                            fontFamily: "var(--ff-mono)",
                          }}
                        >
                          {s.symbol}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {s.company_name}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {s.industry}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {s.is_archived ? (
                            <Pill tone="info">archived</Pill>
                          ) : s.scan_excluded ? (
                            <Pill tone="warn">scan-excluded</Pill>
                          ) : (
                            <Pill tone="up">active</Pill>
                          )}
                        </td>
                        <td
                          style={{
                            padding: "var(--s-2) var(--s-3)",
                            fontFamily: "var(--ff-mono)",
                            fontSize: "var(--fs-xs)",
                          }}
                        >
                          {s.sql_table_name_suffix}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </Card>

        {/* ── Upload History ───────────────────────────────────────── */}
        <Card>
          <div
            style={{
              padding: "var(--s-4) var(--s-5)",
              borderBottom: "1px solid var(--border-1)",
            }}
          >
            <h2 style={{ margin: 0 }}>Upload history</h2>
          </div>
          <div style={{ padding: "var(--s-5)" }}>
            {loadingUploads ? (
              <p className="t-body-sm" style={{ color: "var(--t-3)" }}>
                Loading upload history…
              </p>
            ) : uploads.length === 0 ? (
              <p className="t-body-sm" style={{ color: "var(--t-3)" }}>
                No uploads recorded.
              </p>
            ) : (
              <div style={{ overflowX: "auto" }}>
                <table
                  style={{
                    width: "100%",
                    borderCollapse: "collapse",
                    fontSize: "var(--fs-sm)",
                  }}
                >
                  <thead>
                    <tr style={{ borderBottom: "1px solid var(--border-1)", textAlign: "left" }}>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Date</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Rows</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>New</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Archived</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Excluded</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>HDS</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Status</th>
                      <th style={{ padding: "var(--s-2) var(--s-3)" }}>Rollback</th>
                    </tr>
                  </thead>
                  <tbody>
                    {uploads.map((u) => (
                      <tr
                        key={u.id}
                        style={{
                          borderBottom: "1px solid var(--border-1)",
                          opacity: u.rolled_back ? 0.6 : 1,
                        }}
                      >
                        <td
                          style={{
                            padding: "var(--s-2) var(--s-3)",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {formatDate(u.uploaded_at)}
                          <br />
                          <span className="t-body-sm" style={{ color: "var(--t-3)" }}>
                            {formatAge(u.uploaded_at)}
                          </span>
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {u.total_rows}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {u.new_symbols.length > 0 ? (
                            <Pill tone="brand">{u.new_symbols.length}</Pill>
                          ) : (
                            <span className="t-body-sm" style={{ color: "var(--t-3)" }}>
                              —
                            </span>
                          )}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {u.rows_archived > 0 ? (
                            <Pill tone="warn">{u.rows_archived}</Pill>
                          ) : (
                            <span className="t-body-sm" style={{ color: "var(--t-3)" }}>
                              —
                            </span>
                          )}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {u.rows_excluded > 0 ? u.rows_excluded : "—"}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {u.hds_triggered ? (
                            <Pill tone="brand">Triggered</Pill>
                          ) : (
                            <span className="t-body-sm" style={{ color: "var(--t-3)" }}>
                              —
                            </span>
                          )}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {u.rolled_back ? (
                            <Pill tone="info">
                              Rolled back{" "}
                              {u.rolled_back_at
                                ? formatAge(u.rolled_back_at)
                                : ""}
                            </Pill>
                          ) : (
                            <Pill tone="up">Active</Pill>
                          )}
                        </td>
                        <td style={{ padding: "var(--s-2) var(--s-3)" }}>
                          {!u.rolled_back && (
                            <Btn
                              size="sm"
                              variant="ghost"
                              onClick={() => handleRollback(u.id)}
                              disabled={rollbacking === u.id}
                            >
                              {rollbacking === u.id ? "Rolling back…" : "Rollback"}
                            </Btn>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </Card>
      </div>
    </Shell>
  );
}
