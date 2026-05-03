import type { ReactNode } from "react";
import type { Metadata } from "next";
import Link from "next/link";
import { LegalFooter } from "@/components/primitives";

export const metadata: Metadata = {
  title: "Legal — Signal Stack",
};

export default function LegalLayout({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        minHeight: "100vh",
        background: "var(--bg-0)",
      }}
    >
      {/* header */}
      <header
        style={{
          display: "flex",
          alignItems: "center",
          gap: "10px",
          padding: "20px 24px",
          borderBottom: "1px solid var(--line-1)",
        }}
      >
        <Link
          href="/"
          style={{
            textDecoration: "none",
            color: "var(--fg-1)",
            fontFamily: "var(--font-display)",
            fontWeight: 700,
            fontSize: "16px",
            letterSpacing: "-0.01em",
          }}
        >
          Signal Stack
        </Link>
        <span style={{ color: "var(--fg-3)", fontSize: "14px" }}>/</span>
        <span style={{ color: "var(--fg-3)", fontSize: "14px" }}>Legal</span>
      </header>

      {/* content */}
      <main style={{ flex: 1 }}>{children}</main>

      {/* footer */}
      <LegalFooter />
    </div>
  );
}
