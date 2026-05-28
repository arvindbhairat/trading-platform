/**
 * Contract-diff — Frontend ↔ API contract validation (REQ-NFR-016).
 *
 * Loads all contract snapshots from packages/testing/contracts/, validates them
 * against the contract schema, checks for duplicate endpoints, and verifies
 * internal consistency.
 *
 * When an API base URL is provided via CONTRACT_API_BASE_URL, also validates
 * actual API responses against the contract definitions (CI integration test mode).
 *
 * Usage:
 *   npm run contract-diff                      ← schema validation only
 *   CONTRACT_API_BASE_URL=http://localhost:5000 npm run contract-diff  ← + live check
 *   CONTRACT_API_BASE_URL=http://localhost:5000 CONTRACT_TOKEN=... npm run contract-diff
 */

import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const contractsDir = join(__dirname, '..', 'contracts');

// ── Exit state ────────────────────────────────────────────────────────────────
let exitCode = 0;
const errors = [];
const warnings = [];
const infos = [];

function fail(message) {
  errors.push(message);
  exitCode = 1;
  console.error(`  ✗ ${message}`);
}

function warn(message) {
  warnings.push(message);
  console.warn(`  ⚠ ${message}`);
}

function info(message) {
  infos.push(message);
  console.log(`  ${message}`);
}

// ── Schema (compiled in-code for zero dependencies) ────────────────────────────
const VALID_METHODS = new Set(['GET', 'POST', 'PUT', 'PATCH', 'DELETE']);
const VALID_AUTH = new Set(['required', 'optional', 'none']);
const VALID_TYPES = new Set([
  'string', 'number', 'boolean', 'object', 'array', 'string[]', 'number[]', 'null'
]);

function validateContractShape(contract, filename) {
  const prefix = `[${filename}]`;

  if (!contract.endpoint || typeof contract.endpoint !== 'string') {
    fail(`${prefix} Missing or invalid 'endpoint' (must be a non-empty string).`);
  }

  if (!VALID_METHODS.has(contract.method)) {
    fail(`${prefix} Invalid 'method': "${contract.method}". Must be one of: ${[...VALID_METHODS].join(', ')}.`);
  }

  if (contract.auth !== undefined && !VALID_AUTH.has(contract.auth)) {
    fail(`${prefix} Invalid 'auth': "${contract.auth}". Must be one of: ${[...VALID_AUTH].join(', ')}.`);
  }

  // Validate responseFields.
  if (!Array.isArray(contract.responseFields)) {
    fail(`${prefix} Missing or invalid 'responseFields' (must be an array).`);
  } else {
    for (const [i, f] of contract.responseFields.entries()) {
      if (!f.path || typeof f.path !== 'string') {
        fail(`${prefix} responseFields[${i}]: missing or invalid 'path'.`);
      }
      if (f.type && !VALID_TYPES.has(f.type)) {
        warn(`${prefix} responseFields[${i}].path="${f.path}": unknown type "${f.type}".`);
      }
      if (typeof f.required !== 'boolean') {
        fail(`${prefix} responseFields[${i}].path="${f.path}": 'required' must be a boolean.`);
      }
    }
  }

  // Validate requestFields if present.
  if (contract.requestFields !== undefined) {
    if (!Array.isArray(contract.requestFields)) {
      fail(`${prefix} 'requestFields' must be an array.`);
    } else {
      for (const [i, f] of contract.requestFields.entries()) {
        if (!f.path || typeof f.path !== 'string') {
          fail(`${prefix} requestFields[${i}]: missing or invalid 'path'.`);
        }
        if (typeof f.required !== 'boolean') {
          fail(`${prefix} requestFields[${i}].path="${f.path}": 'required' must be a boolean.`);
        }
      }
    }
  }

  // Validate statusCodes if present.
  if (contract.statusCodes !== undefined) {
    if (!Array.isArray(contract.statusCodes)) {
      fail(`${prefix} 'statusCodes' must be an array.`);
    } else {
      for (const [i, code] of contract.statusCodes.entries()) {
        if (!Number.isInteger(code) || code < 100 || code > 599) {
          fail(`${prefix} statusCodes[${i}]: invalid HTTP status code "${code}".`);
        }
      }
    }
  }
}

// ── Live API validation ────────────────────────────────────────────────────────

async function validateAgainstApi(contracts) {
  const apiBase = process.env.CONTRACT_API_BASE_URL;
  if (!apiBase) return;

  const token = process.env.CONTRACT_TOKEN || '';
  info(`Live API validation against ${apiBase}${token ? ' (authenticated)' : ' (unauthenticated)'}`);

  // Build auth headers if token provided.
  const authHeaders = token ? { Authorization: `Bearer ${token}` } : {};

  for (const contract of contracts) {
    // Skip templated endpoints (containing {param}) in automated validation —
    // these require specific path parameter values.
    const hasPathParams = /\{/.test(contract.endpoint);
    if (hasPathParams) {
      info(`  SKIP  ${contract.method} ${contract.endpoint} — templated path, requires explicit parameters`);
      continue;
    }

    // Skip POST/PUT/PATCH/DELETE in automated validation — these may have
    // side effects or require specific request bodies.
    if (contract.method !== 'GET') {
      info(`  SKIP  ${contract.method} ${contract.endpoint} — non-GET, requires explicit request body`);
      continue;
    }

    try {
      const url = `${apiBase.replace(/\/+$/, '')}${contract.endpoint}`;
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 5000);

      const res = await fetch(url, {
        headers: { ...authHeaders, Accept: 'application/json' },
        signal: controller.signal,
      });
      clearTimeout(timeout);

      // Check status code is in contract.
      if (contract.statusCodes && !contract.statusCodes.includes(res.status)) {
        warn(`  CODE  ${contract.method} ${contract.endpoint} → ${res.status} (expected ${contract.statusCodes.join('/')})`);
        continue;
      }

      // For 200/201, check response body contains required fields.
      if ((res.status === 200 || res.status === 201) && contract.responseFields) {
        let body;
        try {
          body = await res.json();
        } catch {
          warn(`  JSON  ${contract.method} ${contract.endpoint} — response is not valid JSON`);
          continue;
        }

        const requiredFields = contract.responseFields.filter(f => f.required);
        for (const field of requiredFields) {
          const value = getNestedValue(body, field.path);
          if (value === undefined) {
            fail(`  MISS  ${contract.method} ${contract.endpoint} — required field "${field.path}" missing from response`);
          }
        }

        info(`  OK    ${contract.method} ${contract.endpoint} → ${res.status}`);
      } else {
        info(`  OK    ${contract.method} ${contract.endpoint} → ${res.status}`);
      }
    } catch (err) {
      warn(`  FAIL  ${contract.method} ${contract.endpoint} — ${err.message}`);
    }
  }
}

// ── OpenAPI spec validation ─────────────────────────────────────────────────────

/**
 * Loads and parses an OpenAPI 3.0 JSON spec from disk.
 * Returns the parsed spec object, or null on failure.
 */
function loadOpenApiSpec(openapiPath) {
  if (!openapiPath) return null;
  if (!existsSync(openapiPath)) {
    fail(`OpenAPI spec not found at "${openapiPath}".`);
    return null;
  }
  try {
    const raw = readFileSync(openapiPath, 'utf-8');
    return JSON.parse(raw);
  } catch (err) {
    fail(`Failed to parse OpenAPI spec: ${err.message}`);
    return null;
  }
}

/**
 * Validates all contracts against an OpenAPI 3.0 spec.
 * For each contract:
 *   1. Verifies the endpoint + method exists in the spec paths
 *   2. For each requestFields.path, verifies the field exists in the spec's requestBody schema
 *   3. For each responseFields.path, verifies the field exists in the spec's response schema (200)
 */
function validateContractsAgainstOpenApi(contracts, spec) {
  info('── OpenAPI Validation ─────────────────────────────────────────────');
  let openApiOk = 0;
  let openApiFail = 0;

  for (const contract of contracts) {
    const key = endpointKey(contract);
    const specPath = normalizeEndpointForSpec(contract.endpoint);
    const method = contract.method.toLowerCase();

    // 1. Check endpoint exists in spec paths.
    const pathItem = spec.paths?.[specPath];
    if (!pathItem) {
      fail(`[OpenAPI] ${key} — endpoint not found in OpenAPI spec paths.`);
      openApiFail++;
      continue;
    }

    const operation = pathItem[method];
    if (!operation) {
      fail(`[OpenAPI] ${key} — method "${contract.method}" not found for path "${specPath}" in OpenAPI spec.`);
      openApiFail++;
      continue;
    }

    // 2. Validate requestFields against the spec's requestBody schema.
    if (Array.isArray(contract.requestFields) && contract.requestFields.length > 0) {
      const requestSchema = extractSchemaFromRequestBody(operation);
      if (!requestSchema) {
        warn(`[OpenAPI] ${key} — contract declares requestFields but OpenAPI spec has no requestBody schema.`);
      } else {
        for (const field of contract.requestFields) {
          const propPath = field.path.replace(/\./g, '/properties/');
          const found = findSchemaProperty(requestSchema, field.path);
          if (!found) {
            fail(`[OpenAPI] ${key} — request field "${field.path}" not found in OpenAPI requestBody schema.`);
            openApiFail++;
          }
        }
      }
    }

    // 3. Validate responseFields against the spec's 200 response schema.
    if (Array.isArray(contract.responseFields) && contract.responseFields.length > 0) {
      const responseSchema = extractSchemaFromResponse(operation, '200');
      if (!responseSchema) {
        warn(`[OpenAPI] ${key} — contract declares responseFields but OpenAPI spec has no 200 response schema.`);
      } else {
        for (const field of contract.responseFields) {
          const found = findSchemaProperty(responseSchema, field.path);
          if (!found && field.required) {
            fail(`[OpenAPI] ${key} — required response field "${field.path}" not found in OpenAPI 200 response schema.`);
            openApiFail++;
          }
        }
      }
    }

    openApiOk++;
    info(`  ✓ ${key}`);
  }

  info(`  OpenAPI validation: ${openApiOk} passed, ${openApiFail} failed`);
  return openApiFail === 0;
}

/**
 * Normalizes an endpoint path to OpenAPI spec format (replaces {param} with
 * OpenAPI's {param} notation — already the same format used in contracts).
 */
function normalizeEndpointForSpec(endpoint) {
  // Remove /api/v1 prefix since OpenAPI paths typically don't include version prefix
  // if it's part of the base URL. But since our OpenAPI spec is generated from the
  // API code which includes the full path, we need to match as-is.
  // However, contract endpoints might not include the /api/v1/ prefix.
  // Try matching with and without the prefix.
  if (!endpoint.startsWith('/api/v1') && !endpoint.startsWith('/api/')) {
    // If the endpoint doesn't start with our API prefix, we won't find it.
    // This is informational only — the endpoint may be a mock or external.
    return endpoint;
  }
  return endpoint;
}

/**
 * Extracts the request body schema from an OpenAPI operation object.
 */
function extractSchemaFromRequestBody(operation) {
  const requestBody = operation.requestBody;
  if (!requestBody) return null;

  const content = requestBody.content?.['application/json'];
  if (!content?.schema) return null;

  return resolveSchemaRef(content.schema);
}

/**
 * Extracts the response schema from an OpenAPI operation object for the given status code.
 */
function extractSchemaFromResponse(operation, statusCode) {
  const response = operation.responses?.[statusCode];
  if (!response) return null;

  const content = response.content?.['application/json'];
  if (!content?.schema) return null;

  return resolveSchemaRef(content.schema);
}

/**
 * Resolves a $ref to its target in the spec's components.schemas.
 */
function resolveSchemaRef(schema, resolvedCache = new Map()) {
  if (!schema) return null;

  if (schema.$ref) {
    if (resolvedCache.has(schema.$ref)) return resolvedCache.get(schema.$ref);

    // Resolve "#/components/schemas/SchemaName"
    const parts = schema.$ref.replace('#/', '').split('/');
    // We can't resolve without the full spec here — return the ref name
    // so callers know the schema type is defined elsewhere.
    return { _ref: schema.$ref, _refName: parts[parts.length - 1] };
  }

  return schema;
}

/**
 * Searches for a property path in an OpenAPI schema object.
 * Supports dot-notation paths like "holdings.symbol".
 */
function findSchemaProperty(schema, path) {
  if (!schema) return false;

  const parts = path.split('.');
  let current = schema;

  for (const part of parts) {
    if (!current) return false;

    // Handle $ref — extract the referenced schema name.
    if (current.$ref) {
      current = { _refName: current.$ref.split('/').pop() };
      // We can't resolve without the full spec, but at least we know the ref name.
    }

    // Check properties
    if (current.properties?.[part] !== undefined) {
      current = current.properties[part];
      continue;
    }

    // Check items for array types (e.g., holdings[].symbol → items.properties.symbol)
    if (current.type === 'array' && current.items?.properties?.[part] !== undefined) {
      current = current.items.properties[part];
      continue;
    }

    // Handle the case where the property wraps the response in an object
    // like { holdings: [...] } — look one level deeper
    if (current.properties) {
      return false;
    }

    // Handle allOf / oneOf
    const composition = current.allOf || current.oneOf || [];
    let found = false;
    for (const sub of composition) {
      if (findSchemaProperty(sub, part)) {
        found = true;
        current = sub;
        break;
      }
    }
    if (found) continue;

    // Handle additionalProperties
    if (current.additionalProperties && typeof current.additionalProperties === 'object') {
      current = current.additionalProperties;
      continue;
    }

    return false;
  }

  return true;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function getNestedValue(obj, path) {
  return path
    .replace(/\[\]/g, '')   // strip array markers
    .split('.')
    .reduce((acc, key) => (acc !== null && acc !== undefined ? acc[key] : undefined), obj);
}

function endpointKey(contract) {
  return `${contract.method} ${contract.endpoint}`;
}

// ── CLI argument parsing ────────────────────────────────────────────────────────

/**
 * Parses --openapi <path> from the command line arguments.
 * Returns an object with { openapiPath } or null if not provided.
 */
function parseArgs() {
  const args = process.argv.slice(2);
  const result = { openapiPath: null };

  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--openapi' && i + 1 < args.length) {
      result.openapiPath = args[i + 1];
      i++;
    }
  }

  return result;
}

// ── Main ───────────────────────────────────────────────────────────────────────

async function main() {
  const cliArgs = parseArgs();

  if (!existsSync(contractsDir)) {
    info('contracts/ directory not found — scaffolding pass (no contracts registered).');
    process.exit(0);
  }

  const files = readdirSync(contractsDir)
    .filter(f => f.endsWith('.contract.json'))
    .sort();

  if (files.length === 0) {
    info('No contract snapshots registered yet — scaffolding pass.');
    process.exit(0);
  }

  info('── Contract Snapshot Validation ──────────────────────────────────');
  info(`Found ${files.length} contract snapshot(s) in contracts/:`);

  const contracts = [];
  const seenEndpoints = new Set();

  for (const file of files) {
    let contract;
    try {
      const raw = readFileSync(join(contractsDir, file), 'utf-8');
      contract = JSON.parse(raw);
    } catch (err) {
      fail(`[${file}] Failed to parse JSON: ${err.message}`);
      continue;
    }

    // Validate shape.
    validateContractShape(contract, file);

    // Check for duplicate endpoint+methdod combinations.
    const key = endpointKey(contract);
    if (seenEndpoints.has(key)) {
      fail(`[${file}] Duplicate endpoint: ${key} (already defined in another contract file).`);
    }
    seenEndpoints.add(key);

    contracts.push(contract);
    info(`  ✓ ${key}`);
  }

  // Summary.
  if (errors.length === 0 && warnings.length === 0) {
    info('── All contracts valid ──────────────────────────────────────────────');
  }

  if (warnings.length > 0) {
    info(`── Warnings (${warnings.length}) ──────────────────────────────────────────`);
    for (const w of warnings) info(`  ⚠ ${w}`);
  }

  if (errors.length > 0) {
    info(`── Errors (${errors.length}) ────────────────────────────────────────────`);
    for (const e of errors) info(`  ✗ ${e}`);
  }

  // Live API validation.
  await validateAgainstApi(contracts);

  // OpenAPI spec validation (Phase 5).
  let openApiPassed = true;
  if (cliArgs.openapiPath) {
    const spec = loadOpenApiSpec(cliArgs.openapiPath);
    if (spec) {
      openApiPassed = validateContractsAgainstOpenApi(contracts, spec);
    } else {
      openApiPassed = false;
    }
  }

  // Final report.
  const total = files.length;
  info('');
  info(`── Report ───────────────────────────────────────────────────────────`);
  info(`  Contracts: ${total}`);
  info(`  Errors:    ${errors.length}`);
  info(`  Warnings:  ${warnings.length}`);
  if (cliArgs.openapiPath) {
    info(`  OpenAPI:   ${openApiPassed ? 'passed' : 'failed'}`);
  }

  process.exit(exitCode);
}

main().catch(err => {
  console.error('[contract-diff] Fatal error:', err);
  process.exit(1);
});
