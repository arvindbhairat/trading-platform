import type { CSSProperties } from "react";

// Long-form Disclaimer — versioned document (REQ-LEGAL-007)
// Current version: v1 (Phase A)

export default function DisclaimerPage() {
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
          Disclaimer
        </h1>
      </header>

      <section style={sectionStyle}>
        <h2 style={h2style}>1. No Investment Advice</h2>
        <p style={pStyle}>
          Signal Stack is a decision-support platform. All outputs — including
          signals, alerts, notifications, chart markings, portfolio analytics,
          and risk metrics — are tools-generated information derived from your
          own configured scans, parameters, and risk profile. None of these
          outputs constitute investment advice, a recommendation, a
          solicitation, or an offer to buy or sell any security or financial
          instrument.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>2. Not Registered with SEBI</h2>
        <p style={pStyle}>
          The Platform is operating in private evaluation and is not registered
          with the Securities and Exchange Board of India (SEBI) or any other
          securities regulator. The Platform does not hold a Research Analyst
          registration, Investment Adviser registration, or any other SEBI
          registration.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>3. Self-Directed Decision-Making</h2>
        <p style={pStyle}>
          You are the sole decision-maker. All trading decisions — including
          which securities to buy or sell, when to transact, and at what price
          — are yours alone. You should independently verify any Platform output
          before acting on it and consult a qualified financial adviser if you
          are uncertain.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>4. No Guarantee of Results</h2>
        <p style={pStyle}>
          Past performance of any strategy, signal, or backtest result does not
          guarantee future results. Trading involves substantial risk of loss
          and is not suitable for all investors. You should not trade with money
          you cannot afford to lose.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>5. Limitation of Liability</h2>
        <p style={pStyle}>
          To the maximum extent permitted by law, the Platform operator
          disclaims all liability for any losses, damages, or costs arising from
          your use of the Platform, including but not limited to trading losses,
          data inaccuracies, service interruptions, or delays in market data.
          No representation is made that the Platform will be uninterrupted,
          error-free, or that any outputs are accurate, complete, or timely.
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
        <p>Signal Stack · Disclaimer v1</p>
      </footer>
    </article>
  );
}

const sectionStyle: CSSProperties = { marginBottom: "28px" };
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
