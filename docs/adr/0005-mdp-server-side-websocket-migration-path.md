# ADR-0005 — MDP Server-Side WebSocket: Migration Path from REST-Only Constraint

## Status

**Draft** — documents a known future decision point; no immediate action required in Phase A or B.

---

## Context

The current platform enforces a strict REST-only rule for all backend market data access (REQ-MARKET-002a / REQ-MARKET-002b, CLAUDE.md guardrails). Specifically:

> *All three provider concerns — shared MDP ingestion, Portal Live Data, and User Account Data — are accessed from backend services (API and Worker) exclusively via the provider's REST interface. The FYERS Data WebSocket, and any equivalent streaming interface a future provider exposes, is explicitly out of scope for the backend.*

This constraint was adopted deliberately for FYERS, where the REST Bulk Quotes API is the well-documented and rate-limit-friendly access pattern for batch price polling, and where the WebSocket feed is intended for per-session browser-side streaming. It also keeps the MDP abstraction surface narrow, makes rate-limit accounting uniform, and keeps retry and observability logic centralised in the throttling layer.

However, TrueData and Global Data Feeds — the two candidate commercial providers for the Phase B+ migration — are primarily designed around **server-side WebSocket** delivery as their recommended low-latency quote and tick distribution pattern. Their REST APIs exist but are typically intended for historical data fetch and one-off lookups, not for high-frequency intraday polling. Attempting to replicate LMDS behaviour via polling their REST endpoints may exhaust per-second quotas, introduce unnecessary latency, or be explicitly unsupported under their commercial terms.

This means the REQ-MARKET-002a/b REST-only constraint, which is correct for FYERS, will need to be revisited when the platform migrates to a commercial provider. Without documenting this now, the migration will feel like a spec violation to implementers who treat REQ-MARKET-002 as a permanent platform principle rather than a FYERS-era implementation constraint.

---

## Decision Drivers

1. **The MDP abstraction is the isolation boundary, not the transport protocol.** The goal of REQ-MARKET-002 is to prevent provider-specific transport details from leaking into domain or job code — not to prohibit server-side WebSocket in principle.

2. **REST polling of TrueData/GDF at LMDS cadence is operationally risky.** Polling 500 symbols at 90-second intervals via REST against a provider that expects WebSocket consumers may violate their terms, exhaust rate limits, or produce latency incompatible with stop-level monitoring.

3. **Server-side WebSocket is not inherently more complex than HTTP polling if the transport is fully encapsulated in the adapter.** The channel-based RME serialisation (ADR-0003) is unaffected by whether the price event originates from a REST poll or a WebSocket push — both result in the same `PriceLevelBreachedEvent` on the position channel.

4. **No current action is required.** FYERS is the sole provider in Phase A and Phase B entry. The migration decision belongs to a later ADR that also evaluates which provider is selected and the exact commercial terms.

---

## Decision

**The REST-only constraint in REQ-MARKET-002a/b is a FYERS-era implementation constraint, not a permanent platform principle.**

When the platform migrates to a commercial third-party market data provider (TrueData, Global Data Feeds, or equivalent) ahead of Phase B broader rollout, the following supersession applies:

1. **REQ-MARKET-002a and REQ-MARKET-002b will be superseded** by a new pair of requirements (REQ-MARKET-002d and REQ-MARKET-002e, or equivalent) that allow server-side WebSocket connections in the Worker Service for shared market data ingestion, if and only if the concrete MDP adapter for the chosen commercial provider encapsulates the WebSocket transport entirely — i.e., no job, service, or domain layer has any awareness of whether quotes arrive via REST or WebSocket.

2. **The MDP interface contract is transport-agnostic by design.** The interface (`IMarketDataProvider`) must expose named operations (`FetchHistoricalOhlcv`, `GetLatestQuote`, and a new `SubscribeToLivePrices` or equivalent push-adapter pattern) without surfacing transport semantics. The adapter layer translates between the provider's WebSocket events and the platform's `PriceLevelBreachedEvent` / `RmeEvent` flow before anything reaches the RME.

3. **The FYERS adapter remains REST-only.** The relaxation does not apply to FYERS; REQ-MARKET-002a/b continue to govern the FYERS adapter unchanged. If FYERS remains in the stack alongside a commercial provider (e.g., for user account data), the REST constraint is preserved for the FYERS adapter specifically.

4. **The browser-tier FYERS WebSocket rule is unaffected.** Logged-in users may continue to use the FYERS Data WebSocket in the browser for live chart and quote refresh. This is a separate path from shared server-side ingestion.

5. **A migration ADR is required before any server-side WebSocket adapter is built.** This document records the intent and the supersession path. The actual migration decision — which provider, which transport, what rate-limit and reconnect policy, what observability model — must be made in a dedicated ADR at the time the commercial provider is contracted.

---

## Consequences

### Positive

- Implementers encountering the REST-only constraint during Phase A/B now have a documented rationale for why it exists and a clear signal that it is not permanent.
- The MDP abstraction layer design is confirmed as transport-agnostic, which guides Phase 2 interface design correctly from the start.
- The migration to a commercial provider will be understood as an adapter and requirement update, not a platform-level architectural reversal.

### Trade-offs / Risks

- Server-side WebSocket in the Worker Service introduces a persistent connection that must be managed across Worker restarts, reconnects, and the singleton lease cycle. This is non-trivial and must be designed carefully in the migration ADR — connection lifecycle, message buffering on reconnect, and backpressure handling all need explicit design.
- A server-side WebSocket shared connection in a single-instance Worker is fine for Phase A/B. When Phase C multi-instance partitioning (ADR-0003) is implemented, the WebSocket subscription scope will need to match the per-instance position partition. This interaction must be addressed in the Phase C ADR.

---

## References

- `REQ-MARKET-002a` / `REQ-MARKET-002b` — current REST-only backend constraint
- `REQ-MARKET-002c` — MDP migration guidance (FYERS-specific constraints remain adapter-layer concerns)
- `docs/system-architecture.md` — "Backend access is REST-only" section
- `CLAUDE.md` — "Never bind market data ingestion code directly to a specific provider; always route through the Market Data Provider (MDP) abstraction layer."
- `ADR-0003` — RME per-position event serialisation (channel model is transport-agnostic)
