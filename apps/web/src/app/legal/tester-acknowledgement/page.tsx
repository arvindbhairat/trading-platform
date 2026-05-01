import type { CSSProperties } from "react";

// Phase A Tester Acknowledgement — versioned document (REQ-LEGAL-004)
// Current version: v1 (Phase A)

export default function TesterAcknowledgementPage() {
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
          Version v1 · Phase A — Private Evaluation
        </p>
        <h1 style={{ margin: 0, fontSize: "28px", fontWeight: 700 }}>
          Phase A Tester Acknowledgement
        </h1>
      </header>

      <section style={sectionStyle}>
        <p style={pStyle}>
          By accepting this acknowledgement, you confirm that you understand and
          agree to the following:
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>1. Private Evaluation Phase</h2>
        <p style={pStyle}>
          The platform is operating in private evaluation (Phase A) and is not
          registered with the Securities and Exchange Board of India (SEBI) or
          any other securities regulator. You are accessing the platform as a
          tester during this evaluation phase.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>2. Nature of Platform Outputs</h2>
        <p style={pStyle}>
          All platform outputs — including signals, alerts, notifications, chart
          markings, portfolio analytics, and risk metrics — are tools-generated
          information derived from your own configured scans and risk profile.
          These outputs are not investment advice, research recommendations,
          buy/sell tips, or analyst opinions.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>3. You Are the Decision-Maker</h2>
        <p style={pStyle}>
          You are the sole decision-maker and self-direct all trades. The
          platform operator accepts no responsibility for your trading outcomes.
          You should independently verify any platform output before acting on
          it.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>4. No Guarantee of Results</h2>
        <p style={pStyle}>
          Past performance of any strategy, signal, or backtest result does not
          guarantee future results. Trading involves substantial risk of loss.
          You should not trade with money you cannot afford to lose.
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
        <p>Signal Stack · Phase A Tester Acknowledgement v1</p>
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
