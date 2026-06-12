"use client";

// SymbolSearch — autocomplete search bar for the TopBar header.
// Fetches symbols from /api/v1/universe/symbols?q= and navigates to
// /chart?symbol={symbol} on selection.
//
// Design reference: built as a drop-in replacement for the plain search
// <input> in primitives.tsx TopBar. Uses only design tokens (--bg-*, --fg-*,
// --line-*, --r-*, --s-*, --font-*) with no hardcoded visual values.

import { useEffect, useRef, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import { apiFetch } from "@/lib/auth";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface SymbolResult {
  symbol: string;
  company_name: string;
  is_archived: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export default function SymbolSearch() {
  const router = useRouter();

  const [inputValue, setInputValue] = useState("");
  const [results, setResults] = useState<SymbolResult[]>([]);
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [loading, setLoading] = useState(false);

  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ── Fetch search results from API ─────────────────────────────────────

  const fetchResults = useCallback(async (query: string) => {
    if (query.trim().length < 1) {
      setResults([]);
      setIsOpen(false);
      return;
    }

    setLoading(true);
    try {
      const res = await apiFetch(
        `/api/v1/universe/symbols?q=${encodeURIComponent(query.trim())}`
      );
      if (res.ok) {
        const data = (await res.json()) as SymbolResult[];
        setResults(data.filter((s) => !s.is_archived));
        setIsOpen(data.length > 0);
        setActiveIndex(-1);
      } else {
        setResults([]);
        setIsOpen(false);
      }
    } catch {
      setResults([]);
      setIsOpen(false);
    } finally {
      setLoading(false);
    }
  }, []);

  // ── Debounced search ──────────────────────────────────────────────────

  const handleInputChange = (value: string) => {
    setInputValue(value);

    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    if (value.trim().length < 1) {
      setResults([]);
      setIsOpen(false);
      return;
    }

    debounceRef.current = setTimeout(() => {
      fetchResults(value);
    }, 250);
  };

  // ── Navigate to chart page ────────────────────────────────────────────

  const navigateToSymbol = useCallback(
    (symbol: string) => {
      setIsOpen(false);
      setInputValue("");
      // Navigate to chart page with bare NSE symbol for sharable links.
      router.push(`/chart?symbol=${symbol}`);
    },
    [router]
  );

  // ── Keyboard navigation ───────────────────────────────────────────────

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isOpen || results.length === 0) {
      if (e.key === "Enter" && inputValue.trim().length > 0) {
        // No results dropdown — navigate directly with input value.
        const val = inputValue.trim().toUpperCase();
        navigateToSymbol(val);
      }
      return;
    }

    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        setActiveIndex((prev) =>
          prev < results.length - 1 ? prev + 1 : 0
        );
        break;
      case "ArrowUp":
        e.preventDefault();
        setActiveIndex((prev) =>
          prev > 0 ? prev - 1 : results.length - 1
        );
        break;
      case "Enter":
        e.preventDefault();
        if (activeIndex >= 0 && activeIndex < results.length) {
          navigateToSymbol(results[activeIndex].symbol);
        } else if (inputValue.trim().length > 0) {
          navigateToSymbol(inputValue.trim().toUpperCase());
        }
        break;
      case "Escape":
        e.preventDefault();
        setIsOpen(false);
        setActiveIndex(-1);
        break;
    }
  };

  // ── Close dropdown on outside click ───────────────────────────────────

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (
        inputRef.current &&
        !inputRef.current.contains(e.target as Node) &&
        listRef.current &&
        !listRef.current.contains(e.target as Node)
      ) {
        setIsOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // ── Clean up debounce on unmount ──────────────────────────────────────

  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  // ── Scroll active item into view ──────────────────────────────────────

  useEffect(() => {
    if (activeIndex >= 0 && listRef.current) {
      const item = listRef.current.children[activeIndex] as HTMLElement | undefined;
      item?.scrollIntoView({ block: "nearest" });
    }
  }, [activeIndex]);

  // ── Render ─────────────────────────────────────────────────────────────

  return (
    <div style={{ flex: 1, maxWidth: 480, margin: "0 auto", position: "relative" }}>
      <span
        style={{
          position: "absolute",
          left: 10,
          top: "50%",
          transform: "translateY(-50%)",
          color: "var(--fg-3)",
          pointerEvents: "none",
          zIndex: 1,
        }}
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="11" cy="11" r="8" />
          <line x1="21" y1="21" x2="16.65" y2="16.65" />
        </svg>
      </span>
      <input
        ref={inputRef}
        value={inputValue}
        onChange={(e) => handleInputChange(e.target.value)}
        onKeyDown={handleKeyDown}
        onFocus={() => {
          if (results.length > 0) setIsOpen(true);
        }}
        placeholder="Search symbol or company · RELIANCE"
        style={{
          width: "100%",
          background: "var(--bg-2)",
          border: "1px solid var(--line-1)",
          color: "var(--fg-1)",
          padding: "7px 12px 7px 32px",
          borderRadius: "var(--r-sm)",
          fontFamily: "var(--font-sans)",
          fontSize: 13,
          outline: "none",
        }}
        autoComplete="off"
        spellCheck={false}
      />
      {loading && (
        <span
          style={{
            position: "absolute",
            right: 10,
            top: "50%",
            transform: "translateY(-50%)",
            color: "var(--fg-3)",
            fontSize: 11,
          }}
        >
          Loading…
        </span>
      )}

      {/* Dropdown */}
      {isOpen && results.length > 0 && (
        <ul
          ref={listRef}
          style={{
            position: "absolute",
            top: "100%",
            left: 0,
            right: 0,
            margin: "4px 0 0",
            padding: "4px 0",
            listStyle: "none",
            background: "var(--bg-2)",
            border: "1px solid var(--line-1)",
            borderRadius: "var(--r-sm)",
            boxShadow: "0 4px 12px rgba(0,0,0,0.15)",
            maxHeight: 360,
            overflow: "auto",
            zIndex: 1000,
          }}
        >
          {results.map((item, i) => (
            <li
              key={item.symbol}
              onClick={() => navigateToSymbol(item.symbol)}
              onMouseEnter={() => setActiveIndex(i)}
              style={{
                padding: "8px 12px 8px 32px",
                cursor: "pointer",
                background:
                  i === activeIndex ? "var(--bg-4)" : "transparent",
                display: "flex",
                alignItems: "center",
                gap: 10,
              }}
            >
              <span
                style={{
                  fontFamily: "var(--font-mono)",
                  fontSize: 13,
                  fontWeight: 600,
                  color: "var(--fg-1)",
                  whiteSpace: "nowrap",
                }}
              >
                {item.symbol.replace(/-EQ$/, "")}
              </span>
              <span
                style={{
                  fontSize: 12,
                  color: "var(--fg-3)",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                  whiteSpace: "nowrap",
                }}
              >
                {item.company_name}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
