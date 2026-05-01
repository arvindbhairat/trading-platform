import type { CSSProperties } from "react";

// Terms of Service — versioned document (REQ-LEGAL-008)
// Current version: v1 (Phase A)

export default function TosPage() {
  return (
    <article
      style={{
        maxWidth: "720px",
        margin: "0 auto",
        padding: "40px 24px 60px",
        color: "var(--fg-1)",
      }}
    >
      <header style={{ marginBottom: "32px" }}>
        <p
          style={{
            fontSize: "12px",
            fontWeight: 600,
            letterSpacing: "0.08em",
            textTransform: "uppercase",
            color: "var(--fg-3)",
            marginBottom: "8px",
          }}
        >
          Version v1 · Effective as of platform launch
        </p>
        <h1 style={{ margin: 0, fontSize: "28px", fontWeight: 700 }}>
          Terms of Service
        </h1>
      </header>

      <section style={sectionStyle}>
        <h2 style={h2style}>1. Acceptance of Terms</h2>
        <p style={pStyle}>
          By accessing or using the Signal Stack platform ("the Platform"), you
          agree to be bound by these Terms of Service ("Terms"). If you do not
          agree to all of these Terms, you must not access or use the Platform.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>2. Description of Service</h2>
        <p style={pStyle}>
          The Platform provides decision-support tools, including charting,
          signal research, backtesting, portfolio analytics, and notifications.
          The Platform is not a brokerage, exchange, or execution venue. All
          trades are executed independently by you through your broker. The
          Platform does not place orders on your behalf.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>3. Eligibility</h2>
        <p style={pStyle}>
          You must be at least 18 years of age to use the Platform. By using the
          Platform, you represent and warrant that you are 18 or older. The
          Platform is intended for use only in jurisdictions where its
          functionality complies with applicable law. You are responsible for
          determining whether your use is lawful in your jurisdiction.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>4. User Responsibilities</h2>
        <p style={pStyle}>
          You are solely responsible for: (a) maintaining the confidentiality of
          your account credentials, including your OAuth login and FYERS API
          token; (b) all activity that occurs under your account; (c) all
          investment decisions and trading activity; and (d) compliance with all
          applicable laws and regulations.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>5. No Investment Advice</h2>
        <p style={pStyle}>
          The Platform generates tools-derived outputs based on your configured
          parameters, scans, and risk profile. These outputs are for
          informational and research purposes only and do not constitute
          investment advice, a recommendation, or a solicitation to buy or sell
          any security. You are the sole decision-maker. Past performance of any
          strategy or signal does not guarantee future results.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>6. Limitation of Liability</h2>
        <p style={pStyle}>
          To the maximum extent permitted by applicable law, the Platform
          operator shall not be liable for any direct, indirect, incidental,
          special, consequential, or punitive damages arising out of or relating
          to your use of the Platform, including but not limited to trading
          losses, lost profits, or data loss.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>7. Termination</h2>
        <p style={pStyle}>
          The Platform operator reserves the right to suspend or terminate your
          access to the Platform at any time, with or without cause, including
          for violation of these Terms or applicable law. Upon termination, your
          right to use the Platform immediately ceases.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>8. Changes to Terms</h2>
        <p style={pStyle}>
          These Terms may be updated from time to time. When the version is
          bumped, you will be required to accept the updated Terms on your next
          login before accessing any protected Platform feature. Continued use
          of the Platform after acceptance of updated Terms constitutes your
          agreement to the changes.
        </p>
      </section>

      <footer
        style={{
          marginTop: "48px",
          paddingTop: "16px",
          borderTop: "1px solid var(--line-1)",
          fontSize: "12px",
          color: "var(--fg-3)",
        }}
      >
        <p>Signal Stack · Terms of Service v1</p>
      </footer>
    </article>
  );
}

const sectionStyle: CSSProperties = {
  marginBottom: "28px",
};

const h2style: CSSProperties = {
  fontSize: "18px",
  fontWeight: 600,
  marginBottom: "10px",
  color: "var(--fg-1)",
};

const pStyle: CSSProperties = {
  fontSize: "14px",
  lineHeight: 1.7,
  color: "var(--fg-2)",
  margin: 0,
};
