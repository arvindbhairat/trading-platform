# NSE Trading Platform Build Guide for Claude

## Mission

Build a secure, testable web-based trading platform focused on the Nifty 500 universe listed on NSE.
The platform is a unified browser-based system for charting, Signal research, backtesting, portfolio analytics, Telegram notifications, admin controls, and user-initiated execution assistance.

## Read Order

Start with these files in order:

1. [docs/README.md](docs/README.md)
2. [docs/terminology.md](docs/terminology.md)
3. [docs/requirements-spec.md](docs/requirements-spec.md)
4. [docs/product-scope.md](docs/product-scope.md)
5. [docs/system-architecture.md](docs/system-architecture.md)
6. [docs/implementation-roadmap.md](docs/implementation-roadmap.md)
7. [docs/engineering-standards.md](docs/engineering-standards.md)
8. [docs/system-config.md](docs/system-config.md)
9. [docs/portfolio-risk-guidelines.md](docs/portfolio-risk-guidelines.md)
10. [docs/data-management.md](docs/data-management.md)
11. [docs/repo-structure.md](docs/repo-structure.md)
12. [docs/project-structure.md](docs/project-structure.md)

Then, when implementing a task, load only the skill file relevant to the task. Use `docs/project-structure.md` as the practical map to locate where code belongs.

## Documentation Rules

- `docs/terminology.md` is the canonical reference for all component names and abbreviations; use it before naming any new component, collection, or domain concept.
- `docs/requirements-spec.md` is the canonical source of truth for product requirements.
- `docs/system-architecture.md` explains how the system is shaped to satisfy the requirements.
- `docs/implementation-roadmap.md` sequences delivery work and must not invent new requirements.
- `docs/engineering-standards.md` holds reusable engineering conventions, not feature requirements.
- `.claude/skills/*` are execution aids and must remain short and derived from the canonical docs.
- significant design trade-offs should be recorded in `docs/adr`.

## Default Technical Direction

Unless the repository evolves in another direction, use these defaults:

- Developer profile: primary developer is proficient in C# and TypeScript, knows some HTML and CSS, and is new to Node.js full-stack patterns — keep frontend simple and push all business logic to the .NET backend
- Frontend: Next.js + TypeScript (Node.js app; chosen for TypeScript-first beginner accessibility; keep it thin — no business logic, no risk calculations)
- Charting: TradingView `lightweight-charts` (self-hosted JS library, renders OHLCV data from SQL Server)
- Fundamental data widgets: TradingView embeddable widgets (Financials, Fundamental Data, Company Profile) loaded as iframe embeds on the chart page; data sourced from TradingView servers, no API key required, uses `NSE:{symbol}` format
- Backend API: ASP.NET Core + C#
- Realtime: WebSockets
- Operational database: MongoDB
- Historical database: SQL Server
- Cache and queues: Redis where needed
- Background jobs: .NET worker services or Quartz/Hangfire-style .NET scheduling where appropriate
- Auth: OAuth with Google, Microsoft, and Facebook/Meta
- Market Data Provider (MDP): configurable abstraction layer; initial supported providers are FYERS (admin daily token), TrueData, and Global Data Feeds; active provider is admin-configured; used by background jobs only (DataSync (DS), HistoricDataSeed (HDS), EOD Signal Runner (EODSR)). FYERS is the initial provider because its market-data APIs are free during the testing and evaluation phase only; the platform will migrate to a commercial third-party provider (TrueData, GDF, or equivalent) ahead of broader rollout. All FYERS-specific quirks must remain encapsulated behind the MDP abstraction so the migration is a configuration and adapter change — see REQ-MARKET-002a/b/c. Backend services access all market data and account data exclusively through REST; the FYERS Data WebSocket is permitted in the browser tier only.
- Portal live quotes and real-time chart data: fetched using the individual logged-in user's FYERS token at display time; historical OHLCV served from SQL Server
- User account data (positions, orders, trades, profile): always FYERS via individual user token
- Execution assistance: FYERS API Connect JS widget (branded button SDK); order parameters are pre-populated by the platform frontend and passed to the widget; the FYERS-hosted pop-up handles final submission; the platform backend must never call the FYERS order placement REST API directly
- Logging in .NET apps: `ILogger` with Serilog
- Cross-system observability: OpenTelemetry with OTLP export through a stable collector or gateway
- Shared technical configuration: Azure App Configuration
- Secrets: environment variables and Azure Key Vault
- Admin-managed runtime settings: MongoDB `sys_config`
- Source control and CI/CD: GitHub + GitHub Actions
- Hosting: Azure

## Non-Negotiable Guardrails

- Never implement anything outside NSE Nifty 500 unless the requirements spec changes.
- Never implement short-side workflows in the current platform.
- Never design the platform to place orders autonomously.
- Never use a regular user's FYERS token for shared market-data ingestion regardless of which market data provider is configured.
- Never bind market data ingestion code directly to a specific provider; always route through the Market Data Provider (MDP) abstraction layer.
- Never store user-specific operational state in SQL Server.
- Never blur the line between entry signal generation and user-initiated execution assistance.
- Never bypass server-side validation for auth, Signal, portfolio, or admin-sensitive behavior.
- Never let timeframe logic diverge across charting, backtesting, and the EOD Signal Runner.
- Never make startup-critical configuration depend only on MongoDB.
- Never introduce secrets into source control, fixtures, or docs.
- Never deploy the Worker Service as more than one running instance until the Phase C multi-instance partitioning extension in ADR-0003 is delivered and accepted in a new ADR. Azure App Service scale-out, auto-scale rules, and VM scale-sets must remain disabled for the Worker Service. Scale-out silently breaks the per-position channel invariant and causes concurrent writes on the same position document. The API service is unaffected and may scale horizontally.
- Never introduce custom styles in `apps/web` outside the design system tokens defined in `globals.css`. All colors, spacing, font sizes, and shadows must use design tokens. All UI components must reuse or extend `src/components/primitives.tsx`. Before implementing any UI, consult `design_system/mock_screens/` and `design_system/preview/`. See `docs/engineering-standards.md` § "Design System Standards" for the full rules.

## Working Style

- prefer small vertical slices with demonstrable outcomes
- treat Signal execution, auth, credentials, permissions, and portfolio accounting as high-risk areas
- update the canonical docs when a requirement actually changes
- keep implementations simple, typed, observable, and testable
- avoid speculative rewrites when incremental delivery is possible
- enforce the design system strictly: start every UI change from `design_system/mock_screens/`, map components from `src/components/primitives.tsx`, and use tokens from `globals.css` — never hardcode visual values
