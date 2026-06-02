/**
 * CSS variable resolution for libraries that don't support `var()`.
 *
 * lightweight-charts, canvas-based charting libraries, and other
 * third-party tools parse color strings internally and cannot evaluate
 * CSS custom properties. This utility reads the computed value of a
 * CSS variable from the document root so we can pass real hex/rgb
 * strings to those libraries while keeping the design token system.
 *
 * Usage:
 *   import { cssVar } from "@/lib/css-vars";
 *   createChart(el, { layout: { textColor: cssVar("--fg-2") } });
 */

let _root: HTMLElement | null = null;

function getRoot(): HTMLElement {
  if (!_root) {
    _root = typeof document !== "undefined" ? document.documentElement : null;
  }
  return _root!;
}

/**
 * Resolve a CSS custom property to its computed string value.
 * Returns the raw color string (e.g. "#c4bdb0") or `fallback` if
 * the variable is not defined or we're on the server.
 */
export function cssVar(name: string, fallback = "#000000"): string {
  if (typeof document === "undefined") return fallback;
  return getComputedStyle(getRoot()).getPropertyValue(name).trim() || fallback;
}
