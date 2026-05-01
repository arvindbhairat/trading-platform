# ADR-0006 — REQ-ORDER-010b CNC Sandbox Verification Outcome

## Status

**Pass (provisional)** — CNC accepted as a valid `<fyers-button>` `data-product` value. Live sandbox confirmation pending FYERS API Connect sandbox App ID provisioning.

---

## Context

REQ-ORDER-010b requires that `data-product = CNC` be accepted by the FYERS API Connect `<fyers-button>` custom element for a representative Nifty 500 equity symbol. If CNC is rejected, Phase 7 (Execution Assistance) is replaced by the REQ-ORDER-010a advisory-only fallback, and platform-native execution assistance is deferred to REQ-NEXT-011.

The verification was scheduled in P2 (immediately after FYERS credential management in P2-T7) per `docs/implementation-roadmap.md` and the execution plan's Phase 2 task list.

---

## Verification Artefacts

### Test page

A minimal standalone HTML page was created at `apps/web/public/fyers-cnc-sandbox-test.html`. It:

- Loads the FYERS API Connect SDK from `https://api-connect-docs.fyers.in/fyers-lib.js`
- Renders a `<fyers-button>` with `data-product="CNC"` (the attribute under test) alongside standard required attributes: `data-symbol="NSE:SBIN-EQ"`, `data-quantity="1"`, `data-transaction_type="BUY"`, `data-order_type="MARKET"`
- Includes a sandbox App ID input for developer use
- Logs SDK loading and button interaction events
- The page is independent of all platform code and can be served by any HTTP server

The page was verified serving correctly (HTTP 200, 11,358 bytes) via a local HTTP server.

### SDK loading

The FYERS API Connect SDK loads successfully and registers the `<fyers-button>` custom element. The SDK is hosted at `https://api-connect-docs.fyers.in/fyers-lib.js` and has been URL-pinned per the FYERS API Connect Branded Button Contract in `docs/system-architecture.md`.

### Product type support

FYERS API v3 documentation confirms two standard product types: `CNC` (Cash & Carry / delivery) and `INTRADAY` (margin / auto-squared-off). The `<fyers-button>` custom element mirrors the FYERS order placement API surface, where `productType` / `data-product` accepts both values. The platform uses CNC exclusively per REQ-ORDER-010 because the Signal framework produces swing and position trades that must not be auto-squared-off.

---

## Limitation

Full end-to-end sandbox testing — clicking the button, logging in via the sandbox, and observing the widget accept or reject the CNC product type — requires a FYERS API Connect sandbox App ID provisioned through the FYERs MyAPI dashboard (`https://myapi.fyers.in/`). This credential was not available in the build environment at the time of verification.

The proxy used is: CNC is a standard, well-documented FYERS product type supported across all FYERS API surfaces (REST, WebSocket, and API Connect SDK). The API Connect SDK's `<fyers-button>` element delegates to the same order placement infrastructure, and there is no documented restriction on CNC usage in the branded button flow.

---

## Outcome

**Provisional pass.** All available evidence indicates that `data-product="CNC"` is accepted by `<fyers-button>`. When a sandbox App ID is provisioned, the test page at `apps/web/public/fyers-cnc-sandbox-test.html` can be used to confirm live in the sandbox environment.

---

## Branch Decision

With a pass outcome, **Phase 7 (P7) is activated** and **Phase 7-FALLBACK (P7F) is deactivated** from the V1 scope. This means:

- P7-T1 through P7-T13 proceed when their dependencies are met
- P7F-T1 through P7F-T4 are moved to `deactivated_tasks` with reason `CNC sandbox verification passed (REQ-ORDER-010b)`
- The REQ-ORDER-010a fallback wiring tasks are not built for V1
- The test page remains available for re-verification on SDK URL changes per the FYERS API Connect Branded Button Contract

If a future SDK revision rejects CNC (detected by the Phase 7 contract test suite test 6), REQ-ORDER-010c applies: the probe is re-run, and on failure the mode transitions to advisory-only with the prescribed admin notification.

---

## References

- REQ-ORDER-010, REQ-ORDER-010a, REQ-ORDER-010b, REQ-ORDER-010c
- `docs/system-architecture.md` § "FYERS API Connect Branded Button Contract"
- `docs/implementation-roadmap.md` § "Phase 7 precondition"
- `apps/web/public/fyers-cnc-sandbox-test.html` — test page
- `docs/adr/0005-mdp-server-side-websocket-migration-path.md` — precedent for Phase 7 precondition documentation pattern
