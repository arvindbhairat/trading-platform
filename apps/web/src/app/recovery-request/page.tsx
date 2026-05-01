import type { CSSProperties } from "react";
import Link from "next/link";

// REQ-RECOVERY-004: public (no-auth) account recovery intake page.
// Lists the three verification items the user must provide and explains
// the email-based request process to the grievance officer.

export default function RecoveryRequestPage() {
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
        <span style={{ color: "var(--fg-3)", fontSize: "14px" }}>
          Account Recovery
        </span>
      </header>

      {/* content */}
      <main style={{ flex: 1 }}>
        <article
          style={{
            maxWidth: "720px",
            margin: "0 auto",
            padding: "40px 24px 60px",
            color: "var(--fg-1)",
          }}
        >
          <header style={{ marginBottom: "32px" }}>
            <h1 style={{ margin: 0, fontSize: "28px", fontWeight: 700 }}>
              Account Recovery Request
            </h1>
            <p
              style={{
                fontSize: "13px",
                color: "var(--fg-3)",
                marginTop: "8px",
              }}
            >
              Lost access to your account? Submit a recovery request to our
              Grievance Officer.
            </p>
          </header>

          <section style={sectionStyle}>
            <h2 style={h2style}>How It Works</h2>
            <p style={pStyle}>
              If you have lost access to your account (e.g., your OAuth provider
              is unavailable or your account has been compromised), you can
              request an account recovery by emailing the Grievance Officer with
              the following three verification items. These items help us
              confirm your identity and prevent unauthorised access to your
              account.
            </p>
          </section>

          <section style={sectionStyle}>
            <h2 style={h2style}>Required Verification Items</h2>
            <p style={pStyle}>
              To initiate a recovery request, you must provide all three of the
              following:
            </p>
            <ol style={olStyle}>
              <li>
                <strong>Original OAuth Email</strong> — The email address you
                used to sign in via Google, Microsoft, or Facebook when you
                first registered on the platform.
              </li>
              <li>
                <strong>FYERS Account Number or Client ID</strong> — Your FYERS
                trading account number or client ID linked to this platform
                account.
              </li>
              <li>
                <strong>Recent Trade Reference</strong> — A recent trade
                identifier, such as a trade ID, order ID, or trade date with
                symbol and quantity, that helps us verify your trading activity.
              </li>
            </ol>
            <div
              style={{
                background: "var(--warn-bg)",
                border: "1px solid rgba(230,162,60,0.3)",
                borderRadius: "var(--r-lg)",
                padding: "16px 20px",
                marginTop: "16px",
              }}
            >
              <p
                style={{
                  margin: 0,
                  fontSize: "13px",
                  color: "var(--fg-2)",
                  lineHeight: 1.6,
                }}
              >
                <strong>Important:</strong> Recovery requests missing any of the
                three verification items cannot be processed. Incomplete
                requests will be returned with an explanation of what additional
                information is needed.
              </p>
            </div>
          </section>

          <section style={sectionStyle}>
            <h2 style={h2style}>Submitting Your Request</h2>
            <p style={pStyle}>
              Email the Grievance Officer from the email address you originally
              used to register (if still accessible). Include all three
              verification items listed above in your email. Send your request
              to:
            </p>
            <div
              style={{
                background: "var(--bg-2)",
                border: "1px solid var(--line-1)",
                borderRadius: "var(--r-lg)",
                padding: "20px 24px",
                marginTop: "16px",
              }}
            >
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <tbody>
                  <tr>
                    <td style={tdLabel}>Officer</td>
                    <td style={tdValue}>Grievance Officer</td>
                  </tr>
                  <tr>
                    <td style={tdLabel}>Email</td>
                    <td style={tdValue}>
                      <code style={codeStyle}>[GRIEVANCE_EMAIL]</code>
                    </td>
                  </tr>
                  <tr>
                    <td style={tdLabel}>Subject</td>
                    <td style={tdValue}>
                      <code style={codeStyle}>
                        Account Recovery Request — [Your Name]
                      </code>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <ul style={ulStyle}>
              <li>
                <strong>Acknowledgement:</strong> Within 7 calendar days of
                receipt.
              </li>
              <li>
                <strong>Resolution:</strong> Within 30 calendar days of receipt.
              </li>
            </ul>
          </section>

          <section style={sectionStyle}>
            <h2 style={h2style}>What Happens Next</h2>
            <ol style={olStyle}>
              <li>
                The Grievance Officer acknowledges receipt of your request
                within 7 calendar days.
              </li>
              <li>
                Your verification items are checked against our records. If any
                item is incorrect or missing, you will be notified and asked to
                provide corrected information.
              </li>
              <li>
                Once all three items are verified, the Grievance Officer
                initiates the admin-assisted rebind process to restore access to
                your account with a new OAuth identity.
              </li>
              <li>
                You will receive confirmation once the recovery is complete.
                You may then sign in using the new OAuth identity.
              </li>
            </ol>
          </section>

          <section style={sectionStyle}>
            <h2 style={h2style}>Still Need Help?</h2>
            <p style={pStyle}>
              If you have additional questions about the recovery process,
              please review our{" "}
              <Link href="/legal/privacy" style={linkStyle}>
                Privacy Policy
              </Link>{" "}
              or contact the Grievance Officer via the{" "}
              <Link href="/legal/grievances" style={linkStyle}>
                Contact and Grievances
              </Link>{" "}
              page.
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
            <p>Signal Stack · Account Recovery Request</p>
          </footer>
        </article>
      </main>
    </div>
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
const olStyle: CSSProperties = {
  fontSize: "14px",
  lineHeight: 1.8,
  color: "var(--fg-2)",
  paddingLeft: "20px",
  marginTop: "12px",
};
const ulStyle: CSSProperties = {
  fontSize: "14px",
  lineHeight: 1.8,
  color: "var(--fg-2)",
  paddingLeft: "20px",
  marginTop: "12px",
};
const tdLabel: CSSProperties = {
  padding: "8px 16px 8px 0",
  fontSize: "12px",
  fontWeight: 600,
  letterSpacing: "0.06em",
  textTransform: "uppercase",
  color: "var(--fg-3)",
  verticalAlign: "top",
  width: "100px",
};
const tdValue: CSSProperties = {
  padding: "8px 0",
  fontSize: "14px",
  color: "var(--fg-1)",
};
const codeStyle: CSSProperties = {
  background: "var(--bg-3)",
  padding: "2px 6px",
  borderRadius: "var(--r-xs)",
  fontSize: "13px",
};
const linkStyle: React.CSSProperties = {
  color: "var(--brand-300)",
  textDecoration: "none",
};
