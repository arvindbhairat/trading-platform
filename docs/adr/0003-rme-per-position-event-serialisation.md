# ADR-0003 — RME Per-Position Event Serialisation

## Status

**Accepted**

---

## Context

The Risk Management Engine (RME) is event-driven. Multiple background workers generate events that the RME must process for the same position record concurrently:

| Event source | What it produces | RME action on position |
|---|---|---|
| Live Market Data Scan (LMDS) | Price breach detected (stop, add, reduce level) | Update advisory flags on the Open position (set `exit advisory active`, `add advisory active`, or `reduce advisory active` per REQ-PLC-002 flag list); transition to `Suspended` only when a circuit/extreme-gap event warrants it; write notification |
| Live Account Data Scan (LADS) | Confirmed trade fill detected | PendingEntry → Open, recalculate all levels; or update quantity/avg price after partial fill |
| EOD Signal Runner (EODSR) | New entry signal for symbol with existing PendingEntry | Supersede old PendingEntry (→ Rejected), create new PendingEntry |
| EOD stop-level update job | Trailing stop recalculation after session close | Update `trailing_stop_level` and `add_level` / `reduce_level` fields |
| Portfolio heat recalculation | Drawdown threshold crossed; sector exposure breached | Update advisory flags; potentially block new entry |
| Admin actions | Kill switch; user suspension; account deactivation | Transition all active PendingEntry → Suspended |

These event sources run in the same .NET Worker Service process but on independent timers and threads. During active NSE market hours LMDS and LADS run simultaneously; their poll cycles can overlap with each other and with EOD boundary jobs.

The risk of uncontrolled concurrency on a position document:

- **Data race on state and flag updates:** LMDS sets an exit advisory flag (and in extreme cases transitions the position to `Suspended`) while LADS simultaneously processes a confirmed fill for the same position, leaving the position record with a coherent state field but incoherent advisory flags, quantity, and average entry price.
- **Stale-read advisory computation:** A trailing stop update reads the position document at version N; a fill confirmation writes a new average entry price at the same moment; the trailing stop update overwrites it with a value computed from stale entry data.
- **Duplicate notification writes:** Two concurrent breach evaluations emit two stop-breach notifications for the same event because neither has yet set the advisory flag before the other reads it.
- **Lost update on advisory flags:** Two concurrent writers each read the same advisory-flag state, each compute their change independently, and each write back — the second write silently discards the first writer's change.

Without an explicit serialisation model, these races will appear in production under normal market-hours conditions. They are extremely difficult to reproduce in testing because the timing window is narrow.

### Scale context

- Phase A ceiling: 30 users, maximum ~150 open positions platform-wide.
- Phase C estimate: several hundred users, potentially 1,000–2,000 open positions.
- Worker Service runs as a **single instance** in Phase A. Multi-instance scaling is a Phase C concern.

---

## Decision Drivers

1. **Correctness over performance.** `REQ-NFR-001` explicitly prioritises correctness and reproducibility. A wrong stop level or a missed state transition has direct financial consequences.
2. **Determinism required.** `REQ-RME-003` states identical inputs must always produce identical outputs. Non-determinism introduced by concurrency violates this requirement.
3. **Simplicity for a single experienced .NET developer.** The primary developer is strong in C#. Any concurrency primitive must be idiomatic in .NET and well-supported by the toolchain.
4. **Redis is already in the stack** (`REQ-DATA-006`). It can provide cross-instance coordination without adding a new infrastructure dependency.
5. **MongoDB already stores positions.** Optimistic concurrency control using a version field is a first-class pattern in the MongoDB .NET driver.
6. **The solution must be testable in isolation.** The RME is independently testable per `docs/engineering-standards.md`.

---

## Considered Options

### Option A — Optimistic Concurrency Control (OCC) only

Add a `_version` (long, monotonic) field to every position document. All RME writes include a filter clause `{ _id: positionId, _version: expectedVersion }`. If the matched document count is zero, the writer re-reads the document and retries its computation from the fresh state.

**Pros:**
- No external dependency beyond MongoDB (already present).
- Idiomatic with the MongoDB .NET driver (`ReplaceOptions { IsUpsert = false }` + matched-count check).
- Easy to test: inject a version-mismatch scenario in unit tests.
- Scales to multi-instance deployment without code changes.

**Cons:**
- Does not prevent two writers from computing their changes against stale state simultaneously — it only detects the conflict at write time. Under high contention, both writers may run expensive RME calculations before one is forced to retry.
- Retry loops must be bounded; unbounded retries under persistent contention are a liveness hazard.
- Does not guarantee ordering: if LADS and LMDS both produce events for the same position in the same second, the one that wins the write race does not necessarily correspond to the chronologically earlier event. For state machines with directional transitions (e.g., Open → Suspended is irreversible within the RME — only an admin action can unsuspend), an out-of-order win can apply a later event before an earlier one, leaving the position in a logically inconsistent state.

**Verdict:** Necessary as the universal write guard, but insufficient alone as the serialisation mechanism for state-machine transitions.

---

### Option B — Redis Distributed Lock per Position

Before processing any RME event for a position, acquire a Redis lock keyed by `rme:pos:{positionId}` with a short TTL (e.g., 10 seconds). Process the event, write the result, release the lock. Any concurrent writer that fails to acquire the lock either queues the event for retry or skips the cycle (depending on event type).

**Pros:**
- True serialisation: only one writer processes a given position at a time.
- Correct even under multi-instance deployment.
- Redis is already in the stack.
- Redis distributed lock is well-supported via `StackExchange.Redis` and standard Redlock patterns.

**Cons:**
- Lock acquisition adds latency to every RME event. At Phase A scale (150 positions, sub-second LMDS cycles) this is negligible, but must be measured.
- Lock TTL must be long enough to cover worst-case processing time, but short enough that a crashed holder does not block the position indefinitely. Getting this wrong produces either a correctness bug (lock expires mid-write) or an availability bug (position stuck locked after crash).
- Redis is now a correctness dependency, not just a performance dependency. A Redis outage blocks all RME event processing, not just WebSocket fan-out and caching. The degradation behaviour must be explicitly designed (see Consequences).
- Does not naturally enforce event ordering — two events for the same position may still arrive in any order at the lock boundary. Ordering must be enforced separately if needed.

**Verdict:** Correct and sufficient, but introduces Redis as a hard correctness dependency and requires careful TTL calibration.

---

### Option C — In-process Channel per Position (Single Instance Only)

In the .NET Worker Service, maintain a `ConcurrentDictionary<Guid, Channel<RmeEvent>>` mapping each active position ID to a bounded `System.Threading.Channels.Channel<RmeEvent>`. All event producers (LMDS handler, LADS handler, etc.) enqueue events to the position's channel rather than calling the RME directly. A single dedicated consumer task per position drains the channel sequentially, processing one event at a time.

**Pros:**
- True in-process serialisation with zero external dependency.
- Natural event ordering (FIFO per channel).
- `System.Threading.Channels` is a first-class .NET library, idiomatic and well-documented.
- No network round-trips for serialisation — purely in-memory.
- Very easy to unit test (inject events, assert ordered outcomes).
- Channel backpressure is built in (bounded channel drops or blocks the producer on overflow).

**Cons:**
- **Single-instance only.** If the Worker Service scales to two instances, each has its own channel dictionary and events for the same position can be processed concurrently on different instances. This model breaks silently and catastrophically under horizontal scaling.
- Position channel lifecycle management: channels must be created when a position becomes active and destroyed when it closes. This requires a registry with correct cleanup on position close, restart recovery, and crash resilience.
- In-memory state is lost on worker restart. Any enqueued but unprocessed events are dropped. A restart mid-event-processing could leave a position in an intermediate state that the re-started worker cannot detect without re-reading MongoDB.

**Verdict:** Excellent for Phase A single-instance deployment. Requires OCC as a safety net for crash-recovery correctness. Must be replaced or extended before horizontal scaling.

---

### Option D — Event Sourcing with Ordered Event Log per Position

Instead of updating the position document directly, the RME appends event records to a `rme_events` collection (which already exists per `docs/data-management.md`) and derives current position state by replaying the log. A single sequential processor per position replays the log to compute transitions.

**Pros:**
- Perfect audit trail (already required by `REQ-PLC-004`).
- Deterministic replay: position state can always be reconstructed from the event log.
- Ordering is explicit in the log (timestamp + sequence number).

**Cons:**
- Full event sourcing for the position state machine is a significant architectural commitment that increases implementation complexity well beyond the current phase.
- Read-path performance: deriving current position state by replaying the full event log on every query is expensive without a snapshot mechanism.
- The existing position document model (which stores derived state like `trailing_stop_level`, `current_r_multiple`, etc.) already embeds computed state — retrofitting to a pure event-source model would require rearchitecting the position persistence model entirely.

**Verdict:** The `rme_events` collection should continue as the immutable audit log for all RME state transitions (as specified). Full event sourcing for the live position read-model is out of scope for Phase A–C.

---

## Decision

**Adopt a two-layer serialisation model: in-process Channel per position (Option C) as the primary serialisation mechanism, with Optimistic Concurrency Control (Option A) as the mandatory write-level safety net.**

Redis distributed locking (Option B) is **not adopted** as the primary mechanism because it makes Redis a hard correctness dependency. It **is** specified as the extension path when the Worker Service scales beyond one instance.

### The two-layer model

```
┌───────────────────────────────────────────────────────────────────────┐
│ Worker Service (single instance, Phase A/B)                           │
│                                                                       │
│  Event Producers                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐    │
│  │ LMDS handler │  │ LADS handler │  │ EOD / admin event source │    │
│  └──────┬───────┘  └──────┬───────┘  └─────────────┬────────────┘    │
│         │                 │                         │                 │
│         └─────────────────┴─────────────────────────┘                │
│                           │ enqueue(RmeEvent)                        │
│                           ▼                                           │
│  Position Channel Registry (ConcurrentDictionary<Guid, Channel<T>>)  │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │  pos-A  [event1] → [event2] → [event3]  (bounded, FIFO)       │   │
│  │  pos-B  [event4]                                               │   │
│  │  pos-C  [event5] → [event6]                                    │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                           │ one consumer task per active position     │
│                           ▼                                           │
│  RME Event Processor (sequential per position)                        │
│  1. Read current position from MongoDB                                │
│  2. Apply RME logic (deterministic, same implementation as backtest)  │
│  3. Write result to MongoDB with OCC version check                    │
│     - If version conflict → re-read and retry (max 3 attempts)        │
│     - If still conflicted → log as error, mark position for review    │
│  4. Write to rme_events (immutable audit log)                         │
│  5. Write to notifications if alert generated                         │
└───────────────────────────────────────────────────────────────────────┘
```

### Layer 1 — In-process Channel (primary serialisation)

- The `PositionChannelRegistry` is a singleton service in the Worker Service DI container.
- Each active position (PendingEntry or Open state) has exactly one bounded `Channel<RmeEvent>` in the registry with a configurable capacity (default 50 events; bounded-wait on overflow so producers block rather than drop).
- All RME event producers call `registry.Enqueue(positionId, rmeEvent)`. They never call the RME processor directly.
- One `Task` per active position runs a `while await channel.Reader.ReadAsync()` loop, processing events sequentially. This task is started when the channel is created and cancelled when the position closes.
- **Ordering guarantee:** events for the same position are processed in the order they were enqueued. Cross-position events have no ordering guarantee and need none.
- **Channel lifecycle:**
  - Created: when a new PendingEntry position is created (on entry signal) or when the worker restarts and loads active positions from MongoDB.
  - Destroyed: when the position transitions to Closed or Rejected (terminal states). The channel is completed (no new items accepted) and the consumer task drains any remaining events before terminating.
  - On worker restart: the registry is rebuilt from MongoDB by loading all non-terminal positions at startup. Events that were enqueued but not processed before the crash are replayed by the next LMDS/LADS poll cycle (idempotent re-evaluation).

### Layer 2 — Optimistic Concurrency Control (write guard)

Every position document carries a `_version` field (long, starting at 1, incremented on every write).

Every RME write uses a MongoDB filter of the form:
```
{ _id: <positionId>, _version: <readVersion> }
```
with `UpdateResult.MatchedCount` checked after the update. If `MatchedCount == 0`, the write was rejected because another writer (in a crash-recovery scenario or a future multi-instance deployment) modified the document concurrently. The processor re-reads the document and retries the RME computation from fresh state.

Maximum retry attempts: 3. After 3 consecutive OCC failures on the same position event, the event is logged as an error, the position is flagged for admin review in `rme_events` with reason `concurrency_conflict_unresolved`, and a `position_concurrency_alert` admin notification is written.

The OCC mechanism ensures correctness even if the channel-based serialisation is bypassed (e.g., in crash-recovery code paths that write directly without going through the channel).

### Event model

```csharp
public abstract record RmeEvent
{
    public Guid PositionId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Source { get; init; }  // "LMDS" | "LADS" | "EODSR" | "EOD_STOP" | "ADMIN"
}

// Concrete event types (non-exhaustive):
public record PriceLevelBreachedEvent(Guid PositionId, ..., LevelType Level, decimal Price) : RmeEvent;
public record FillConfirmedEvent(Guid PositionId, ..., decimal FilledQty, decimal AvgFillPrice) : RmeEvent;
public record TrailingStopUpdateEvent(Guid PositionId, ..., decimal NewSessionClose) : RmeEvent;
public record DrawdownStateChangedEvent(Guid PositionId, ..., DrawdownLevel Level) : RmeEvent;
public record KillSwitchActivatedEvent(Guid PositionId, ...) : RmeEvent;
```

All events are immutable records. The RME processor is a pure function over `(PositionDocument, RmeEvent) → (PositionDocument, IReadOnlyList<RmeOutput>)` where `RmeOutput` includes notification records, `rme_events` entries, and the updated position document. This pure-function shape makes the RME trivially unit-testable and deterministic.

---

## Phase C Scaling Extension

When the Worker Service must scale beyond one instance (Phase C), the in-process channel model must be extended with a position claim mechanism:

- Each worker instance claims a partition of active positions using a Redis sorted set keyed by `rme:partition:{instanceId}`, with a TTL-based heartbeat.
- A position is assigned to exactly one instance at a time. If an instance fails (missed heartbeat), its claimed positions are redistributed to healthy instances.
- Each instance maintains its own channel registry for its claimed positions only.
- OCC remains in place as the write guard across instances.

This extension does not require changing the RME processor or the event model — only the channel registry and claim management layer change. Document the scaling path in a separate ADR when Phase C capacity planning begins.

---

## Consequences

### Positive

- The RME is correct under all normal concurrent load scenarios in Phase A (single-instance worker, up to ~150 active positions).
- The pure-function RME processor shape enforces the determinism requirement from `REQ-RME-003` at the architectural level.
- OCC version fields on position documents provide a last-resort safety net for crash-recovery and future multi-instance scenarios without changing correctness semantics.
- The channel model makes event ordering explicit and auditable.
- `rme_events` retains the immutable audit trail of all state transitions as required by existing data management specs.

### Negative / Trade-offs

- **Redis is not a correctness dependency for Phase A/B** — if Redis fails, WebSocket fan-out and caching degrade but RME event processing continues through the channel mechanism. This is an explicit improvement over Option B.
- **In-memory state loss on worker restart.** Events enqueued in channels but not yet processed are lost on crash. Mitigation: LMDS and LADS both poll on short intervals; any event they generated will be re-detected and re-enqueued on the next poll cycle. State machine transitions that were partially applied before the crash are detected by the OCC retry on restart. The worst-case window is one LMDS poll interval of missed breach detection (configurable, default likely 30–60 seconds).
- **Channel backpressure is a new operational signal.** If RME processing is consistently slower than the event ingestion rate (unlikely at Phase A scale but possible if RME computation becomes expensive), channels will fill. Monitor `channel.Reader.Count` as an OpenTelemetry metric. A channel at > 80% capacity should alert.
- **The `_version` field is a new required field on all position documents.** All existing position write paths must be updated to increment and check the version field. This is a migration (add the field with value 0 to all existing documents; the first write upgrades it to 1). Document this in the Phase 6 migration plan.

### Implementation requirements derived from this ADR

The following requirements must be added to `docs/requirements-spec.md` in the RME Core Architecture section before Phase 6 implementation begins:

1. **REQ-RME-CONC-001** — All RME event processing for a given position must be serialised through a per-position in-process channel queue in the Worker Service. No event producer may invoke RME business logic directly; all producers must enqueue an `RmeEvent` record to the position's channel. The channel registry must be initialised from MongoDB at worker startup and must create one channel per non-terminal position.

2. **REQ-RME-CONC-002** — Every position document in MongoDB must carry a `_version` field (long integer, starting at 1, incremented on every write). Every RME write must use an OCC filter including the expected version. If the filter matches zero documents, the processor must re-read the current document and retry the computation up to 3 times. A 4th consecutive failure must log an error, flag the position in `rme_events` with reason `concurrency_conflict_unresolved`, write an admin-only notification, and cease further retry for that event.

3. **REQ-RME-CONC-003** — The RME event processor must be implemented as a pure function over `(PositionDocument, RmeEvent) → (UpdatedPositionDocument, IReadOnlyList<RmeOutput>)`. The function must have no side effects other than returning its output list. All writes to MongoDB, `rme_events`, and `notifications` are performed by the channel consumer loop after receiving the output list, not inside the pure function.

4. **REQ-RME-CONC-004** — The `channel.Reader.Count` (backlog depth) for each active position channel must be exposed as an OpenTelemetry gauge metric tagged by position ID. A platform-wide aggregate (max channel depth across all active positions) must be emitted as a separate gauge. The admin System Health widget (REQ-ADMIN-014) must surface the maximum channel depth alongside the LMDS and LADS health indicators.

5. **REQ-RME-CONC-005** — Position channel lifecycle must be managed as follows: (a) a channel is created immediately when a new PendingEntry position document is written; (b) a channel is completed (writer side closed) immediately when the position transitions to Closed or Rejected, after which the consumer task drains any remaining events before terminating; (c) on worker restart, the registry is rebuilt synchronously from MongoDB before the LMDS and LADS pollers start, so no event can be enqueued for a position whose channel does not yet exist.

---

## Alternatives Rejected

- **Full event sourcing (Option D):** Out of scope for Phase A–C. The `rme_events` audit log fulfils the auditability requirement without requiring a full event-source read-model.
- **Redis distributed lock as primary mechanism (Option B):** Rejected because it makes Redis a hard correctness dependency. Redis remains a performance and fan-out dependency, not a correctness dependency.
- **No concurrency control (status quo):** Rejected. The race conditions are real, predictable under normal market-hours load, and directly harmful to position record accuracy and financial safety.

---

## References

- `REQ-RME-001` through `REQ-RME-021` — RME requirements
- `REQ-PLC-001` through `REQ-PLC-009` — Position lifecycle requirements
- `REQ-STOP-006` — Live Market Data Scan design
- `REQ-PORT-019` through `REQ-PORT-027` — Portfolio sync and LADS design
- `REQ-DATA-006` — Redis in the technology stack
- `REQ-NFR-001` — Correctness over latency
- `REQ-RME-003` — RME determinism requirement
- `docs/engineering-standards.md` — Testing standards (RME must be independently testable)
- `docs/data-management.md` — `rme_events` and `positions` collection specs
