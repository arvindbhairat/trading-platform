# Task Log: FYERS Data WebSocket SDK Migration

**Date**: 2026-06-12
**Task**: Migrate from raw WebSocket to fyers-web-sdk-v3

## Problem

The FYERS Data WebSocket at `wss://socket.fyers.in/data/v3` using the legacy
JSON protocol (`{type: "auth", authorization: "..."}`, `{type: "subscribe", ...}`)
has been deprecated. FYERS v3 now requires the HSM (Hardware Security Module)
binary protocol — a proprietary, obfuscated binary format over WebSocket.

The raw browser `WebSocket` in `live-quotes.ts` could not authenticate or decode
the binary stream, resulting in a permanently disconnected state.

## Solution

Replaced the raw `WebSocket` layer with FYERS's official browser-compatible
SDK: **fyers-web-sdk-v3** (v1.8.0).

The SDK handles:
- JWT-based authentication handshake with HSM key exchange
- Binary message decoding (the HSM protocol)
- Field-name mapping (raw short names → descriptive names)
- Auto-reconnection with configurable retry count
- Singleton connection management

## Files Changed

| File | Change |
|------|--------|
| `apps/web/package.json` | Added `fyers-web-sdk-v3: "^1.8.0"` dependency |
| `apps/web/src/lib/live-quotes.ts` | Replaced raw WebSocket with `fyersDataSocket` |
| `apps/web/src/types/fyers-web-sdk-v3.d.ts` | New: TypeScript declarations for the SDK |

## Key Code Changes

### Before (broken)
```typescript
const ws = new WebSocket("wss://socket.fyers.in/data/v3");
ws.onopen = () => {
  ws.send(JSON.stringify({ type: "auth", authorization: "appId:token" }));
  ws.send(JSON.stringify({ type: "subscribe", symbols: [...], dataType: "SymbolUpdate" }));
};
ws.onmessage = (ev) => {
  const msg = JSON.parse(ev.data);
  if (msg.type === "sf" || msg.type === "if") { /* parse */ }
};
```

### After (working)
```typescript
import { fyersDataSocket } from "fyers-web-sdk-v3";

const token = "appId:accessToken";
const skt = fyersDataSocket.getInstance(token, "", true);

skt.on("connect", () => {
  skt.subscribe(["NSE:SBIN-EQ", "NSE:IDEA-EQ"], false, 1);
  skt.mode(skt.FullMode, 1);
});

skt.on("message", (message) => {
  // Already decoded from HSM binary protocol
  // Fields: { type, ltp, ch, chp, open_price, high_price, low_price,
  //           prev_close_price, vol_traded_today, ..., symbol }
  handleQuote(message);
});

skt.autoReconnect(10);
skt.connect();
```

## Message Field Mapping

The SDK's `HSM/mapper.js` remaps raw binary fields to descriptive names:

| Raw HSM field | Mapped field | LiveQuote property |
|---|---|---|
| `name` | `type` | — (`"sf"` / `"if"`) |
| `ltp` | `ltp` | `ltp` |
| `cng` | `ch` | `change` |
| `nc` | `chp` | `changePct` |
| `op` | `open_price` | `open` |
| `h` | `high_price` | `high` |
| `lo` | `low_price` | `low` |
| `c` | `prev_close_price` | `prevClose` |
| `v` | `vol_traded_today` | `volume` |
| `ltt` | `last_traded_time` | — |
| `fdtm` | `exch_feed_time` | — |

## TypeScript Declarations

Created `src/types/fyers-web-sdk-v3.d.ts` covering:
- `fyersDataSocket` (DataSocket factory with `getInstance`)
- `DataSocketInstance` (`on`, `subscribe`, `unsubscribe`, `mode`, `connect`, `close`, `autoReconnect`)
- `fyersModel` (REST API)
- `fyersOrderSocket` (Order WebSocket)

## Verification

- ✅ `tsc --noEmit` passes with zero errors in `live-quotes.ts`
- ✅ `next build` succeeds — no webpack "Critical dependency" warnings
- ✅ REST fallback logic preserved
- ✅ PLD lease monitoring preserved
- ✅ All public API methods unchanged (`start`, `stop`, `subscribe`, `unsubscribe`, `onQuote`, `onStatus`)
- ✅ New `unsubscribe()` calls SDK's native unsubscribe to reduce bandwidth
