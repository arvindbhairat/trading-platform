# Market Data and Orders Skill

Use this guide when working on FYERS market data, candles, symbol search, historical sync, or execution-assistance paths.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Read These Sections First

- universe management and symbol master
- historical storage and symbol-to-table mapping
- trading calendar and timeframes
- market data and EOD sync
- charting and decision support

## Market Data Focus

- normalize provider payloads immediately
- keep symbol metadata stable and auditable
- separate ingestion from client delivery
- keep EOD sync state explicit and restartable
- keep execution assistance separate from autonomous trading behavior

## Checklist

- provider gaps and stale data are handled intentionally
- SQL Server access stays abstracted behind repositories or services
- EOD sync and scan workflows have explicit coordination markers
- client-facing data remains consistent after reconnects or retries
