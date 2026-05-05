#!/usr/bin/env bash
# ── Language Review Lint ───────────────────────────────────────────────────
# Scans user-facing source files for prohibited terms per REQ-LEGAL-006.
#
# Usage:
#   ./scripts/language-review-lint.sh                    # scan source (exit 0 = clean)
#   ./scripts/language-review-lint.sh --test-fixture     # verify lint catches fixture violations
#
# Prohibited terms: buy, sell, recommended, advised, top pick, suggested pick,
#                   signal to buy, signal to sell, and equivalent phrasing.
# Excluded: apps/web/src/app/legal/ (regulatory/legal document language),
#           __tests__/ (test files), internal code identifiers, XML doc comments.
# ────────────────────────────────────────────────────────────────────────────

set -o pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

EXIT_CODE=0
HAS_ERRORS=0

# ── Prohibited term definitions ────────────────────────────────────────────
# Each entry is "pattern|description"
PATTERNS=(
    "buy|verb 'buy' — use 'open position' or factual reporting"
    "sell|verb 'sell' — use 'close position' or 'reduce position'"
    "recommended|term 'recommended' — implies platform recommendation"
    "advised|term 'advised' — implies platform advice"
    "top pick|phrase 'top pick' — platform does not rank securities"
    "suggested pick|phrase 'suggested pick' — platform does not suggest picks"
    "signal to buy|phrase 'signal to buy' — use 'your scan matched'"
    "signal to sell|phrase 'signal to sell' — use 'your scan matched'"
)

# ── File sets to scan ──────────────────────────────────────────────────────

TSX_FILES=$(find "apps/web/src" -name '*.tsx' \
    ! -path '*/legal/*' \
    ! -path '*/__tests__/*' \
    ! -path '*/node_modules/*' 2>/dev/null || true)

CS_TEMPLATE="apps/api/Notifications/NotificationTemplateBuilder.cs"

# ── Helpers ────────────────────────────────────────────────────────────────

print_header() {
    echo ""
    echo "=== $1 ==="
    echo ""
}

# ── Mode dispatch ──────────────────────────────────────────────────────────

MODE="source"

if [ "${1:-}" = "--test-fixture" ]; then
    MODE="test-fixture"
fi

if [ "$MODE" = "source" ]; then
    # ── Scan source files ──────────────────────────────────────────────────
    print_header "Language Review Lint (REQ-LEGAL-006) — Source Scan"

    for entry in "${PATTERNS[@]}"; do
        pattern="${entry%%|*}"
        description="${entry##*|}"
        found=0

        # Scan .tsx files (portal components)
        if [ -n "$TSX_FILES" ]; then
            while IFS= read -r file; do
                matches=$(grep -n -w -i "$pattern" "$file" 2>/dev/null || true)
                if [ -n "$matches" ]; then
                    while IFS= read -r match; do
                        echo "  ✗ [$description] $file:$match"
                        found=1
                        HAS_ERRORS=1
                    done <<< "$matches"
                fi
            done <<< "$TSX_FILES"
        fi

        # Scan NotificationTemplateBuilder.cs
        if [ -f "$CS_TEMPLATE" ]; then
            matches=$(grep -n -w -i "$pattern" "$CS_TEMPLATE" 2>/dev/null || true)
            if [ -n "$matches" ]; then
                while IFS= read -r match; do
                    echo "  ✗ [$description] $CS_TEMPLATE:$match"
                    found=1
                    HAS_ERRORS=1
                done <<< "$matches"
            fi
        fi

        if [ "$found" -eq 0 ]; then
            echo "  ✓ No '$pattern' violations found"
        fi
    done

    echo ""
    if [ "$HAS_ERRORS" -eq 1 ]; then
        echo "✗ FAILED: Prohibited terms found in user-facing source files."
        echo "  Fix violations or add exclusions. See docs/legal/language-review-checklist.md."
        exit 1
    else
        echo "✓ PASSED: No prohibited terms found in user-facing source files."
        exit 0
    fi

elif [ "$MODE" = "test-fixture" ]; then
    # ── Scan test fixture to verify lint catches violations ────────────────
    print_header "Language Review Lint — Test Fixture Verification"

    FIXTURE="scripts/.language-review-test-fixture.txt"

    if [ ! -f "$FIXTURE" ]; then
        echo "✗ Test fixture not found: $FIXTURE"
        exit 1
    fi

    FIXTURE_TOTAL=0
    FIXTURE_FOUND=0

    for entry in "${PATTERNS[@]}"; do
        pattern="${entry%%|*}"
        description="${entry##*|}"
        FIXTURE_TOTAL=$((FIXTURE_TOTAL + 1))

        matches=$(grep -n -w -i "$pattern" "$FIXTURE" 2>/dev/null || true)
        if [ -n "$matches" ]; then
            echo "  ✓ Caught '$pattern' — $description"
            FIXTURE_FOUND=$((FIXTURE_FOUND + 1))
        else
            echo "  ✗ MISSED '$pattern' — lint did not catch this term!"
            HAS_ERRORS=1
        fi
    done

    echo ""
    echo "  Caught $FIXTURE_FOUND / $FIXTURE_TOTAL prohibited patterns."
    echo ""

    if [ "$HAS_ERRORS" -eq 1 ]; then
        echo "✗ Test fixture FAILED: lint missed some prohibited terms."
        exit 1
    else
        echo "✓ Test fixture PASSED: lint correctly catches all prohibited terms."
        exit 0
    fi
fi
