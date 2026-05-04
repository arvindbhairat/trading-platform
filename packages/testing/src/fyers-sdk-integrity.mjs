/**
 * FYERS SDK integrity verification script (REQ-ORDER-016a).
 *
 * Fetches the SDK from the pinned URL, computes its SHA-256 hash, and
 * compares against the expected hash in the pin file.
 *
 * Usage:
 *   node src/fyers-sdk-integrity.mjs          # check mode — fails on mismatch
 *   node src/fyers-sdk-integrity.mjs --update  # update pin file with current hash
 */

import { readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const PIN_FILE = resolve(__dirname, "..", "data", "fyers-sdk-pin.json");
const FETCH_TIMEOUT_MS = 15_000;

function loadPin() {
  return JSON.parse(readFileSync(PIN_FILE, "utf-8"));
}

function writePin(pin) {
  writeFileSync(PIN_FILE, JSON.stringify(pin, null, 2) + "\n", "utf-8");
}

async function fetchSdk(url) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);

  try {
    const res = await fetch(url, { signal: controller.signal });
    if (!res.ok) {
      throw new Error(`HTTP ${res.status}: ${res.statusText}`);
    }
    const buf = Buffer.from(await res.arrayBuffer());
    return buf;
  } finally {
    clearTimeout(timer);
  }
}

function computeHash(buf) {
  return createHash("sha256").update(buf).digest("hex");
}

async function main() {
  const args = process.argv.slice(2);
  const isUpdate = args.includes("--update");
  const pin = loadPin();

  console.log(`[fyers-sdk-integrity] Pinned URL: ${pin.url}`);
  console.log(`[fyers-sdk-integrity] Expected hash: ${pin.expectedSha256Hash || "(none set)"}`);

  const sdkBuf = await fetchSdk(pin.url);
  const actualHash = computeHash(sdkBuf);
  const actualSize = sdkBuf.length;
  console.log(`[fyers-sdk-integrity] Fetched ${actualSize} bytes`);
  console.log(`[fyers-sdk-integrity] Actual SHA-256: ${actualHash}`);

  if (isUpdate) {
    pin.expectedSha256Hash = actualHash;
    pin.verifiedAt = new Date().toISOString();
    writePin(pin);
    console.log(`[fyers-sdk-integrity] Pin file updated with hash ${actualHash}`);
    return;
  }

  // Check mode
  if (!pin.expectedSha256Hash) {
    console.log(`[fyers-sdk-integrity] WARNING: No expected hash configured yet.`);
    console.log(`[fyers-sdk-integrity] Suggested hash to set: ${actualHash}`);
    console.log(`[fyers-sdk-integrity] Run with --update to set it.`);
    // In CI, an unconfigured hash should still fail to force explicit setup
    process.exitCode = 1;
    return;
  }

  if (actualHash !== pin.expectedSha256Hash) {
    console.error(`[fyers-sdk-integrity] ❌ HASH MISMATCH`);
    console.error(`[fyers-sdk-integrity]   Expected: ${pin.expectedSha256Hash}`);
    console.error(`[fyers-sdk-integrity]   Actual:   ${actualHash}`);
    console.error(`[fyers-sdk-integrity] The FYERS SDK at ${pin.url} has changed.`);
    console.error(`[fyers-sdk-integrity] 1. Review the new SDK revision.`);
    console.error(`[fyers-sdk-integrity] 2. Run the Phase 7 contract-test suite against it.`);
    console.error(`[fyers-sdk-integrity] 3. If it passes, update the pin file with --update and commit.`);
    process.exitCode = 1;
    return;
  }

  console.log(`[fyers-sdk-integrity] ✅ Hash matches expected value.`);
}

main().catch((err) => {
  console.error(`[fyers-sdk-integrity] ❌ ${err.message}`);
  process.exitCode = 1;
});
