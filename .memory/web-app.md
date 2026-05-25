---
name: Web App Architecture
description: Architecture notes for the Next.js frontend
---

## Key Characteristics

- **100% client-rendered** — every page uses `"use client"`, no Server Components
- **No Next.js API routes** — all data fetching goes to the external .NET backend via `NEXT_PUBLIC_API_BASE_URL`
- **No middleware** — no `middleware.ts`
- **No SSR/SSG/ISR** — purely a client-side SPA bootstrapped by Next.js
- **OAuth flow** — JWT stored in sessionStorage, passed as Bearer token
- **WebSocket (browser tier)** — FYERS Data WebSocket (`wss://socket.fyers.in/data/v3`) for live quotes, PLD WebSocket (`/ws/pld`) for lease monitoring
- **Dynamic routes** — backtest `[runId]` page uses client-side hydration
