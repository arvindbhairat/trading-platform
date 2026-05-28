// ── OpenAPI → TypeScript type generator ─────────────────────────────────
// Fetches /openapi/v1.json from the running API and generates TypeScript
// type definitions using openapi-typescript.
//
// Usage: node scripts/typegen-api.mjs
//
// Environment variables:
//   API_BASE_URL — base URL of the running API (default: http://localhost:5000)
//   OUT_PATH     — output path for generated types (default: src/lib/api-types.d.ts)
//
// Phase 4 of the API Contract Standardization Plan.
// See docs/api-contract-standardization-plan.md for full rationale.

import { execSync } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const API_BASE = process.env.API_BASE_URL || 'http://localhost:5000';
const OUT_PATH = resolve(process.env.OUT_PATH || 'src/lib/api-types.d.ts');
const SPEC_PATH = 'openapi.json';

async function main() {
    console.log(`Fetching OpenAPI spec from ${API_BASE}/openapi/v1.json...`);

    const res = await fetch(`${API_BASE}/openapi/v1.json`);
    if (!res.ok) {
        throw new Error(`Failed to fetch OpenAPI spec: ${res.status} ${res.statusText}`);
    }

    const spec = await res.json();
    writeFileSync(SPEC_PATH, JSON.stringify(spec, null, 2));
    console.log(`OpenAPI spec saved to ${SPEC_PATH}`);

    mkdirSync(dirname(OUT_PATH), { recursive: true });

    execSync(`npx openapi-typescript ${SPEC_PATH} -o ${OUT_PATH}`, {
        stdio: 'inherit',
        cwd: process.cwd(),
    });

    console.log(`TypeScript types generated at ${OUT_PATH}`);
}

main().catch((err) => {
    console.error('Type generation failed:', err);
    process.exit(1);
});
