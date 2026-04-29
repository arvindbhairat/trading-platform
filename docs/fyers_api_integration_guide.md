# Fyers API v3 — Integration Guide

**Version:** 3.0.0  
**Last Updated:** April 2026  
**Source:** [https://myapi.fyers.in/docsv3](https://myapi.fyers.in/docsv3)  
**Audience:** Developers building algo trading applications on NSE, BSE, MCX

---

## 1. Overview

Fyers API is a set of REST-like APIs that integrate with the Fyers in-house trading platform. You can place, modify, and cancel orders in real-time, and access account data such as orderbook, tradebook, positions, holdings, and funds. All requests are made exclusively over HTTPS.

- **API Type:** REST + WebSocket (real-time)
- **Base URL (REST):** `https://api-t1.fyers.in/api/v3`
- **Authentication:** OAuth 2.0 (Authorization Code Flow)
- **Rate Limit:** 10 req/sec · 200 req/min · 1,00,000 req/day

---

## 2. Available SDKs

| Language | Version Support |
|----------|----------------|
| Python | 3.8 – 3.12 |
| Node.js | 12 – 21.6.2 |
| Web JS (Browser) | CDN available |
| C# | .NET 8.0.4 |
| Java | Java 8 |
| Go | Go 1.18 |
| C | C11 |

**Install Python SDK:**
```bash
pip install fyers-apiv3
```

**Web JS CDN:**
```html
<script src="https://cdn.fyers.in/js/sdk/1.3.0/fyers-web-sdk-v3/index.min.js"></script>
```

---

## 3. Authentication

### 3.1 App Creation

Before using the API:
1. Log in to [https://myapi.fyers.in/dashboard/](https://myapi.fyers.in/dashboard/)
2. Click **Create App** and provide: App Name, Redirect URL, App Permissions
3. Note your **App ID** (`client_id`, e.g. `SPXXXXE7-100`) and **Secret Key**

**App Permission Templates**

| Permission | Activities |
|-----------|-----------|
| Basic | Profile details, logout |
| Transactions Info | Orders, positions, trades, holdings, funds |
| Order Placement | Place, modify, cancel orders; exit/convert positions |
| Market Data | Historical data, market depth, quotes |

### 3.2 Login Flow (User Apps)

**Step 1 — Generate Auth URL**

```
GET https://api-t1.fyers.in/api/v3/generate-authcode
  ?client_id=SPXXXXE7-100
  &redirect_uri=https://trade.fyers.in/api-login/redirect-uri/index.html
  &response_type=code
  &state=sample_state
```

After the user logs in, they are redirected to your `redirect_uri` with `auth_code` appended.

**Step 2 — Validate Auth Code**

```bash
curl --location --request POST 'https://api-t1.fyers.in/api/v3/validate-authcode' \
--header 'Content-Type: application/json' \
--data-raw '{
  "grant_type": "authorization_code",
  "appIdHash": "<SHA-256 of app_id:app_secret>",
  "code": "<auth_code>"
}'
```

**Response:**
```json
{
  "s": "ok",
  "code": 200,
  "message": "",
  "access_token": "eyJ0eXAiOi...",
  "refresh_token": "eyJ0eXAiO..."
}
```

> **Note:** Refresh tokens will be discontinued from April 1, 2026.

### 3.3 Using the Access Token

Include in every request header as:

```
Authorization: <app_id>:<access_token>
```

Example:
```
Authorization: SPXXXXE7-100:eyJ0eXAiOi...
```

### 3.4 Security Best Practices

- Never share your `app_secret` or `access_token`
- Do not grant trading permissions unless required
- Use a `redirect_uri` in your own control (not `google.com`)
- Verify the returned `state` value matches what you sent
- Never store `app_secret` in the frontend

---

## 4. Request & Response Structure

### Success Response
```json
{
  "s": "ok",
  "code": 200,
  "message": "",
  "<data_key>": { ... }
}
```

### Error Response
```json
{
  "s": "error",
  "code": -16,
  "message": "Error description"
}
```

### HTTP Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Bad request / invalid parameters |
| 401 | Authorization error |
| 403 | Permission error |
| 429 | Rate limit exceeded |
| 500 | Internal server error |

### Common API Error Codes

| Code | Description |
|------|-------------|
| -8 | Token expired |
| -15 | Invalid token |
| -16 | Unable to authenticate user token |
| -17 | Token invalid or expired |
| -50 | One or more invalid parameters |
| -51 | Invalid Order ID |
| -53 | Invalid position ID |
| -99 | Order placement rejected |
| -300 | Invalid symbol (URL-encode special chars e.g. `M%26M`) |
| -352 | Invalid App ID |
| -429 | API rate limit exceeded |

---

## 5. Rate Limits

| Timeframe | Limit |
|-----------|-------|
| Per Second | 10 requests |
| Per Minute | 200 requests |
| Per Day | 1,00,000 requests |

> Users are blocked for the rest of the day if the per-minute limit is exceeded more than 3 times. When rate-limited (HTTP 429), check `Retry-After` (seconds) and `X-Retry-After-Ms` (milliseconds) response headers.

---

## 6. User APIs

### 6.1 Profile
```bash
curl -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/api/v3/profile
```

**Key Response Fields:** `name`, `display_name`, `fy_id`, `email_id`, `pan`, `mobile_number`, `totp`, `pwd_to_expire`, `ddpi_enabled`, `mtf_enabled`

### 6.2 Funds
```bash
curl -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/api/v3/funds
```

Returns `fund_limit` array with titles: Total Balance, Utilized Amount, Clear Balance, Realized P&L, Collaterals, Available Balance, etc.

### 6.3 Holdings
```bash
curl -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/api/v3/holdings
```

**Key Response Fields (per holding):** `symbol`, `holdingType` (HLD/T1), `quantity`, `costPrice`, `marketVal`, `ltp`, `pl`, `isin`, `qty_t1`

**Overall:** `count_total`, `total_investment`, `total_current_value`, `total_pl`, `pnl_perc`

### 6.4 Logout
```bash
curl -X POST -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/api/v3/logout
```
Invalidates the access token for this specific app only.

---

## 7. Transaction APIs

### 7.1 Orderbook
```bash
curl -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/api/v3/orders
```

**Filter by Order ID:** append `?id=<order_id>`  
**Filter by Order Tag:** append `?order_tag=<tag>`

**Key Response Fields:** `id`, `exchOrdId`, `id_fyers`, `symbol`, `qty`, `filledQty`, `remainingQuantity`, `status`, `type`, `side`, `productType`, `limitPrice`, `stopPrice`, `tradedPrice`, `orderDateTime`, `orderValidity`, `orderTag`, `takeProfit`, `stopLoss`

**Order Status Values:**

| Value | Meaning |
|-------|---------|
| 1 | Cancelled |
| 2 | Traded / Filled |
| 4 | Transit |
| 5 | Rejected |
| 6 | Pending |
| 7 | Expired |

### 7.2 Tradebook
```bash
curl -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/api/v3/tradebook
```
**Filter by tag:** append `?order_tag=<tag>`

### 7.3 Positions
```bash
curl -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/api/v3/positions
```

**Key Response Fields:** `symbol`, `id`, `buyAvg`, `buyQty`, `sellAvg`, `sellQty`, `netQty`, `side`, `productType`, `realized_profit`, `pl`, `ltp`

**Overall:** `count_total`, `count_open`, `pl_total`, `pl_realized`, `pl_unrealized`

### 7.4 Order History (Date Range)
```bash
curl -H "Authorization: app_id:access_token" \
  "https://api-t1.fyers.in/api/v3/order-history?from_date=2025-04-01&to_date=2025-12-22&segment_type=0&exchange_type=0&status=0&page_size=100&page_no=1"
```

**Request Parameters:**

| Param | Values |
|-------|--------|
| `segment_type` | 0=All, 1=Equity, 2=Equity Derivatives, 3=Currency, 4=Commodity |
| `exchange_type` | 0=All, 1=NSE, 2=BSE, 3=MCX |
| `status` | 0=All, 1=Executed, 2=Cancelled, 3=Rejected |

### 7.5 Trade History (Date Range)
```bash
curl -H "Authorization: app_id:access_token" \
  "https://api-t1.fyers.in/api/v3/trade-history?from_date=2025-04-01&to_date=2025-12-22&segment_type=0&exchange_type=0&page_size=100&page_no=1"
```

---

## 8. Order Placement

### 8.1 Order Types

| `type` | Description |
|--------|-------------|
| 1 | Limit Order — executes at `limitPrice` or better |
| 2 | Market Order — executes at current market price; set `limitPrice` and `stopPrice` to 0 |
| 3 | Stop Order (SL-M) — becomes market when `stopPrice` is hit |
| 4 | Stop-Limit Order (SL-L) — becomes limit when `stopPrice` is hit |

### 8.2 Product Types

| `productType` | Description |
|---------------|-------------|
| `CNC` | Equity delivery (carry forward) |
| `INTRADAY` | All segments, same-day |
| `MARGIN` | Derivatives, carry forward |
| `CO` | Cover Order — `stopLoss` mandatory (in points) |
| `BO` | Bracket Order — `stopLoss` and `takeProfit` mandatory (in ₹) |
| `MTF` | Margin Trading Facility — approved symbols only |

### 8.3 Sync vs Async APIs

**Sync** (`/sync`) — Returns the exchange `id` immediately. Subject to 10 OPS limit.

**Async** (`/async`) — Queued execution. Returns `id_fyers` immediately (not `id`). Outcomes arrive via Order WebSocket or GET APIs.

### 8.4 Place Single Order (Sync)

**Endpoint:** `POST /api/v3/orders/sync`

```bash
curl -H "Authorization: app_id:access_token" \
  -H "Content-Type: application/json" \
  -X POST -d '{
    "symbol": "NSE:SBIN-EQ",
    "qty": 1,
    "type": 1,
    "side": 1,
    "productType": "INTRADAY",
    "limitPrice": 620,
    "stopPrice": 0,
    "validity": "DAY",
    "disclosedQty": 0,
    "offlineOrder": false,
    "stopLoss": 0,
    "takeProfit": 0,
    "orderTag": "mytag",
    "isSliceOrder": false
  }' https://api-t1.fyers.in/api/v3/orders/sync
```

**Required Fields:** `symbol`, `qty`, `type`, `side`, `productType`, `limitPrice`, `stopPrice`, `validity`, `disclosedQty`, `offlineOrder`

**Response:**
```json
{ "s": "ok", "code": 1101, "message": "Order submitted successfully. Your Order Ref. No.808058117761", "id": "808058117761" }
```

> If `code: 201` is returned, the order request was made but no acknowledgement received — check the orderbook before retrying.

### 8.5 Place Multiple Orders (Sync)

**Endpoint:** `POST /api/v3/multi-order/sync` — Pass an array of up to 10 order objects.

### 8.6 Place MultiLeg Order (Sync)

**Endpoint:** `POST /api/v3/multileg/orders/sync`

```json
{
  "productType": "MARGIN",
  "offlineOrder": false,
  "orderType": "3L",
  "validity": "IOC",
  "legs": {
    "leg1": { "symbol": "NSE:SBIN24JUNFUT", "qty": 750, "side": 1, "type": 1, "limitPrice": 800 },
    "leg2": { "symbol": "NSE:SBIN24JULFUT", "qty": 750, "side": 1, "type": 1, "limitPrice": 800 },
    "leg3": { "symbol": "NSE:SBIN24JUN900CE", "qty": 750, "side": 1, "type": 1, "limitPrice": 3 }
  }
}
```

> `orderType` can be `"2L"` or `"3L"`. All legs must share the same underlying and stream group.

### 8.7 Place Single Order (Async)

**Endpoint:** `POST /api/v3/orders/async` — Same request body as sync.

**Response:**
```json
{ "s": "ok", "code": 1101, "message": "Order queued successfully.", "id_fyers": "c6697c04-..." }
```

### 8.8 Modify Order (Sync)

**Endpoint:** `PATCH /api/v3/orders/sync`

```json
{ "id": "809229222111", "type": 1, "limitPrice": 620, "qty": 1 }
```

### 8.9 Cancel Order (Sync)

**Endpoint:** `DELETE /api/v3/orders/sync`

```json
{ "id": "52009227353" }
```

Alternative — pass ID in path: `DELETE /api/v3/orders/{orderId}/sync`

### 8.10 Exit Position
```bash
# Exit all positions
curl -X DELETE -H "Authorization: app_id:access_token" \
  -d '{"exit_all": 1}' https://api-t1.fyers.in/api/v3/positions

# Exit specific position
curl -X DELETE -H "Authorization: app_id:access_token" \
  -d '{"id": "NSE:SBIN-EQ-INTRADAY"}' https://api-t1.fyers.in/api/v3/positions
```

### 8.11 Convert Position

**Endpoint:** `POST /api/v3/positions`

```json
{
  "symbol": "NSE:SBIN-EQ-INTRADAY",
  "overnight": 0,
  "positionSide": 1,
  "convertQty": 1,
  "convertFrom": "INTRADAY",
  "convertTo": "CNC"
}
```

> CNC, CO, BO, and MTF positions cannot be converted. Cannot convert to CO, BO, or MTF.

### 8.12 Order Tag Rules

- Alphanumeric only — no spaces or special characters
- Length: 1–30 characters
- Cannot be your Client ID or the string `Untagged`
- Not supported for CO and BO orders

### 8.13 Auto-Order Slice

Set `"isSliceOrder": true` to automatically split orders exceeding the exchange freeze-quantity limit. Available for NSE CM, NFO, and BFO only. Maximum 10 slices per request.

---

## 9. GTT Orders

GTT (Good Till Trigger) orders remain active for up to one year.

**Endpoint:** `POST /api/v3/gtt/orders/sync`

**GTT Single:**
```json
{
  "side": 1,
  "symbol": "NSE:SBIN-EQ",
  "productType": "CNC",
  "orderInfo": {
    "leg1": { "price": 1000, "triggerPrice": 1000, "qty": 1 }
  }
}
```

**GTT OCO** (One-Cancels-Other — both SL and Target):
```json
{
  "side": 1,
  "symbol": "NSE:CHOLAFIN-EQ",
  "productType": "CNC",
  "orderInfo": {
    "leg1": { "price": 10000, "triggerPrice": 10000, "qty": 1 },
    "leg2": { "price": 990, "triggerPrice": 990, "qty": 3 }
  }
}
```

> For OCO: `leg1` trigger price must always be **above** LTP; `leg2` trigger price must always be **below** LTP.

**Modify:** `PATCH /api/v3/gtt/orders/sync` with `id` + updated `orderInfo`  
**Cancel:** `DELETE /api/v3/gtt/orders/sync` with `{"id": "..."}` — code 1103 on success  
**Get GTT Orderbook:** `GET /api/v3/gtt/orders`

---

## 10. Smart Orders

Smart orders support advanced automated execution strategies. Max 100 smart orders per day.

| Type | Endpoint | Description |
|------|----------|-------------|
| Limit | `POST /api/v3/smart-order/limit` | Limit order active until a set end time; converts to market or cancels on expiry |
| Trail | `POST /api/v3/smart-order/trail` | Trailing stop-loss that follows the market |
| Step | `POST /api/v3/smart-order/step` | Averages into a position at defined price intervals |
| SIP | `POST /api/v3/smart-order/sip` | Recurring equity investments (daily/weekly/monthly/custom) |

**Lifecycle operations** (all use `flowId`):

| Action | Method | Endpoint |
|--------|--------|----------|
| Modify | PATCH | `/api/v3/smart-order/modify` |
| Cancel | DELETE | `/api/v3/smart-order/cancel` |
| Pause | PATCH | `/api/v3/smart-order/pause` |
| Resume | PATCH | `/api/v3/smart-order/resume` |
| Get All | GET | `/api/v3/smart-order/orderbook` |

> A smart order must be **paused** before it can be modified (except Smart Limit orders).

---

## 11. Margin Calculators

### Span Margin
**Endpoint:** `POST https://api.fyers.in/api/v2/span_margin`

### Multi-Order Margin
**Endpoint:** `POST https://api-t1.fyers.in/api/v3/multiorder/margin`

**Response fields:** `margin_total`, `margin_new_order`, `margin_avail`

---

## 12. Market Data APIs

### 12.1 Quotes
```bash
curl -H "Authorization: app_id:access_token" \
  "https://api-t1.fyers.in/data/quotes?symbols=NSE:SBIN-EQ,NSE:RELIANCE-EQ"
```
Max 50 symbols. Returns `ltp`, `ch`, `chp`, `open_price`, `high_price`, `low_price`, `prev_close_price`, `volume`, `atp`, `bid`, `ask`.

### 12.2 Market Depth
```bash
curl -H "Authorization: app_id:access_token" \
  "https://api-t1.fyers.in/data/depth?symbol=NSE:SBIN-EQ&ohlcv_flag=1"
```
Max 1 symbol. Returns 5-level bid/ask, OHLC, OI, circuit prices.

### 12.3 Historical Data
```bash
curl -H "Authorization: app_id:access_token" \
  "https://api-t1.fyers.in/data/history?symbol=NSE:SBIN-EQ&resolution=D&date_format=1&range_from=2024-01-01&range_to=2024-03-31&cont_flag=1"
```

**Available Resolutions:** `5S`, `10S`, `15S`, `30S`, `45S`, `1`, `2`, `3`, `5`, `10`, `15`, `20`, `30`, `60`, `120`, `240`, `D`, `1W`, `1M`

**Limits:**
- Intraday (1–240 min): up to 100 days per request; data available from July 3, 2017
- Daily/Weekly/Monthly: up to 366 days per request
- Seconds charts: up to 30 trading days

**Response:** `candles` array — each element: `[epoch, open, high, low, close, volume]`

### 12.4 Option Chain
```bash
curl -H "Authorization: app_id:access_token" \
  "https://api-t1.fyers.in/data/options-chain-v3?symbol=NSE:NIFTY50-INDEX&strikecount=5&greeks=1"
```
Returns ATM + N strikes for both CE and PE. Includes `oi`, `volume`, `ltp`, `iv`, `delta`, `gamma`, `theta`, `vega` (when `greeks=1`).

### 12.5 Market Status
```bash
curl -H "Authorization: app_id:access_token" \
  https://api-t1.fyers.in/data/marketStatus
```
Returns status for all exchange-segment combinations: `OPEN`, `CLOSE`, `PREOPEN`, `PREOPEN_CLOSED`, `POSTCLOSE_START`, `POSTCLOSE_CLOSED`.

### 12.6 Symbol Master Files

| Segment | CSV | JSON |
|---------|-----|------|
| NSE Capital Market | `https://public.fyers.in/sym_details/NSE_CM.csv` | `NSE_CM_sym_master.json` |
| NSE F&O | `https://public.fyers.in/sym_details/NSE_FO.csv` | `NSE_FO_sym_master.json` |
| NSE Currency | `https://public.fyers.in/sym_details/NSE_CD.csv` | `NSE_CD_sym_master.json` |
| BSE Capital Market | `https://public.fyers.in/sym_details/BSE_CM.csv` | `BSE_CM_sym_master.json` |
| BSE F&O | `https://public.fyers.in/sym_details/BSE_FO.csv` | `BSE_FO_sym_master.json` |
| MCX Commodity | `https://public.fyers.in/sym_details/MCX_COM.csv` | `MCX_COM_sym_master.json` |

---

## 13. Price Alerts

Price alerts trigger notifications via the General WebSocket when a condition is met.

**Create:** `POST /api/v3/price-alert`
```json
{
  "agent": "fyers-api",
  "alert-type": 1,
  "name": "SBIN Alert",
  "symbol": "NSE:SBIN-EQ",
  "comparisonType": "LTP",
  "condition": "GT",
  "value": 650
}
```

**Comparison Types:** `LTP`, `HIGH`, `LOW`, `OPEN`, `CLOSE`  
**Conditions:** `GT`, `GTE`, `LT`, `LTE`, `==`

**Modify:** `PUT /api/v3/price-alert` (with `alertId`)  
**Delete:** `DELETE /api/v3/price-alert` (with `alertId`)  
**Toggle Enable/Disable:** `PUT /api/v3/toggle-alert` (with `alertId`)  
**Get All:** `GET /api/v3/price-alert`

---

## 14. WebSocket — Real-Time Streaming

### 14.1 Data WebSocket (Market Quotes & Depth)

```python
from fyers_apiv3.FyersWebsocket import data_ws

def onopen():
    fyers_ws.subscribe(symbols=["NSE:SBIN-EQ"], data_type="SymbolUpdate")
    fyers_ws.keep_running()

fyers_ws = data_ws.FyersDataSocket(
    access_token="APPID:access_token",
    log_path="",
    litemode=False,       # True = LTP only (bandwidth efficient)
    write_to_file=False,
    reconnect=True,
    reconnect_retry=10,   # Max 50 retries
    on_connect=onopen,
    on_close=onclose,
    on_error=onerror,
    on_message=onmessage
)
fyers_ws.connect()
```

**Subscription types:** `SymbolUpdate` (full quote), `DepthUpdate` (5-level bid/ask)

**Response message type field (`type`):**

| Value | Meaning |
|-------|---------|
| `sf` | Equity / option data |
| `if` | Index data |
| `dp` | Market depth data |
| `cn` | Connection message |
| `sub` | Subscribe message |

**Lite Mode:** Set `litemode=True` to receive only `ltp`, `symbol`, `type`.

**Subscription limit:** Up to 5,000 symbols per connection (latest SDK versions).

### 14.2 Order WebSocket (Orders, Trades, Positions)

**Endpoint:** `wss://socket.fyers.in/trade/v3`  
**Header:** `Authorization: <app_id>:<access_token>`

```python
from fyers_apiv3.FyersWebsocket import order_ws

fyers_order_ws = order_ws.FyersOrderSocket(
    access_token="APPID:access_token",
    write_to_file=False,
    log_path="",
    on_connect=onopen,
    on_close=onclose,
    on_error=onerror,
    on_orders=onOrder,
    on_trades=onTrade,
    on_positions=onPosition,
    on_general=onGeneral
)
fyers_order_ws.connect()
```

**Subscribe to streams:**
```python
# In the onopen callback:
fyers.subscribe(data_type="OnOrders,OnTrades,OnPositions,OnGeneral")
```

**Subscribe message format (raw):**
```json
{ "T": "SUB_ORD", "SLIST": ["orders", "trades", "positions", "edis", "pricealerts"], "SUB_T": 1 }
```
Set `"SUB_T": -1` to unsubscribe.

### 14.3 TBT WebSocket (Tick-By-Tick — 50-Level Market Depth)

**Endpoint:** `wss://rtsocket-api.fyers.in/versova`  
**Header:** `Authorization: <app_id>:<access_token>`

- Available for **NFO and NSE Equity** instruments only
- Responses are in **protobuf** format
- First packet is a full **snapshot**; subsequent packets are **diffs**
- Proto file: `https://public.fyers.in/tbtproto/1.0.0/msg.proto`
- Compiled files: `https://public.fyers.in/tbtproto/1.0.0/protogencode.zip`

**TBT Rate Limits:**

| Limit | Value |
|-------|-------|
| Active connections per app per user | 3 |
| Symbols per connection (Depth) | 5 |
| Channels per connection | 50 (numbered 1–50) |

**Channel concept:** Subscribe symbols to a channel, then `resume` or `pause` channels to control data flow.

**Subscribe message:**
```json
{
  "type": 1,
  "data": { "subs": 1, "symbols": ["NSE:NIFTY25MARFUT"], "mode": "depth", "channel": "1" }
}
```

**Switch channel:**
```json
{ "type": 2, "data": { "resumeChannels": ["1"], "pauseChannels": ["2"] } }
```

---

## 15. Postback (Webhooks)

Configure a webhook URL in the app dashboard to receive `POST` requests when order status changes (Pending, Cancelled, Rejected, Traded).

- Payload is raw JSON in the request body
- If your server does not return HTTP 200, the URL is blacklisted for 30 minutes
- After 3 consecutive failures the URL is permanently blacklisted until fixed

---

## 16. Symbol Notation

| Segment | Format | Example |
|---------|--------|---------|
| Equity | `{Ex}:{Symbol}-{Series}` | `NSE:SBIN-EQ`, `BSE:SBIN-A` |
| Equity Futures | `{Ex}:{Symbol}{YY}{MMM}FUT` | `NSE:NIFTY20OCTFUT` |
| Equity Options (Monthly) | `{Ex}:{Symbol}{YY}{MMM}{Strike}{CE/PE}` | `NSE:NIFTY20OCT11000CE` |
| Equity Options (Weekly) | `{Ex}:{Symbol}{YY}{M}{dd}{Strike}{CE/PE}` | `NSE:NIFTY2010811000CE` |
| Currency Futures | `{Ex}:{Pair}{YY}{MMM}FUT` | `NSE:USDINR20OCTFUT` |
| Commodity Futures | `{Ex}:{Commodity}{YY}{MMM}FUT` | `MCX:CRUDEOIL20OCTFUT` |

**Weekly Month Codes:** Jan=1, Feb=2…Sep=9, Oct=O, Nov=N, Dec=D

---

## 17. Reference Tables

### Exchanges
| Code | Exchange |
|------|---------|
| 10 | NSE |
| 11 | MCX |
| 12 | BSE |

### Segments
| Code | Segment |
|------|---------|
| 10 | Capital Market |
| 11 | Equity Derivatives |
| 12 | Currency Derivatives |
| 20 | Commodity Derivatives |

### Order Sides
| Value | Meaning |
|-------|---------|
| 1 | Buy |
| -1 | Sell |

### Position Sides
| Value | Meaning |
|-------|---------|
| 1 | Long |
| -1 | Short |
| 0 | Closed |

### Holding Types
| Value | Meaning |
|-------|---------|
| `T1` | Purchased, not yet delivered to demat |
| `HLD` | Available in demat account |

### Order Sources
| Value | Source |
|-------|--------|
| M | Mobile |
| W | Web |
| R | Fyers One |
| A | Admin |
| ITS | API |

---

## 18. Support & Resources

| Resource | Link |
|----------|------|
| API Dashboard | https://myapi.fyers.in/dashboard/ |
| Official Docs | https://myapi.fyers.in/docsv3 |
| Sample Code (GitHub) | https://github.com/FyersDev/fyers-api-sample-code |
| Support Knowledge Base | https://support.fyers.in |
| Community | https://fyers.in/community |
| Python SDK (PyPI) | https://pypi.org/project/fyers-apiv3/ |
| API Support Email | api-support@fyers.in |
