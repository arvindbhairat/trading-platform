# GitHub Copilot Instructions for SignalStack

SignalStack is a unified trading platform for charting, Signal research, backtesting, portfolio analytics, and user-initiated execution assistance on the NSE Nifty 500 universe.

When writing or suggesting code, follow these constraints:

## Non-Negotiable Guardrails
- **NSE Nifty 500 Only**: Long-only universe. No short-side workflows.
- **User-Initiated Execution**: Order parameters are pre-populated by the Next.js web portal and submitted via the FYERS API Connect JS widget. The .NET backend must never place orders directly via REST APIs.
- **Worker Single-Instance**: The background Worker (`apps/worker`) runs as a singleton process coordinated by a Redis lease.
- **Market Data**: Route shared ingestion through the Market Data Provider (MDP) abstraction layer (`IMarketDataProvider`). Use the admin token only.
- **Databases**: PostgreSQL is reserved for historical OHLCV data. MongoDB is used for operational states, user accounts, and system configuration.
- **No Secrets**: Use environment variables and configuration providers.

## Coding Style & Project Structure
- Backend API (`apps/api`) and Worker (`apps/worker`) are ASP.NET Core (.NET 8).
- UI frontend (`apps/web`) is a Next.js 15 client-rendered SPA.
- Business and domain logic live in shared libraries under `packages/` (e.g., `packages/domain`, `packages/storage`, `packages/signals`, `packages/historical`).
- Keep controllers and endpoints thin. Put domain entities and repositories in `packages/`.
- Ensure all logging uses Serilog (`ILogger`) and telemetry uses OpenTelemetry.

## UI Styling rules
- The design system lives in `design_system/`.
- Use design tokens from `globals.css` and primitives from `primitives.tsx`.
- Never use inline styles or hardcode colors/spacings.

## Resumption & Context
- Read `.memory/MEMORY.md` to load recent session context.
- Follow the guidelines in `CLAUDE.md` / `AGENTS.md` and `execution_plan/agent.md`.
