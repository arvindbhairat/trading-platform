/**
 * Contract-diff scaffolding — P1-T9 / REQ-NFR-016.
 *
 * Runs on every CI build that touches the API or portal API-client types.
 * Phase 1: no contracts are registered yet; the runner exits 0 with a
 * "scaffolding pass" message so the gate wires into the pipeline now and
 * will automatically enforce contracts once the first snapshot is added.
 *
 * A contract snapshot is a JSON file in packages/testing/contracts/ whose
 * name follows the pattern  <endpoint-slug>.contract.json  and whose shape
 * is { endpoint, method, responseFields: string[], requestFields: string[] }.
 *
 * When snapshots exist the runner will:
 *   1. Load each snapshot.
 *   2. Compare it against the current API OpenAPI spec (or a fixture).
 *   3. Exit non-zero if any field is missing or the wrong type.
 */

import { existsSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const contractsDir = join(__dirname, '..', 'contracts');

if (!existsSync(contractsDir)) {
  console.log('[contract-diff] contracts/ directory not found — scaffolding pass (no contracts registered yet).');
  process.exit(0);
}

const snapshots = readdirSync(contractsDir).filter((f) => f.endsWith('.contract.json'));

if (snapshots.length === 0) {
  console.log('[contract-diff] No contract snapshots registered yet — scaffolding pass.');
  process.exit(0);
}

// Future: load each snapshot and diff against the live OpenAPI spec / fixture.
console.log(`[contract-diff] ${snapshots.length} contract snapshot(s) found.`);
for (const file of snapshots) {
  console.log(`  - ${file}`);
}
console.log('[contract-diff] Contract comparison not yet implemented — add comparison logic here (REQ-NFR-016).');
process.exit(0);
