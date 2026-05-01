import type { CSSProperties } from "react";

// Contact and Grievances page (REQ-PRIVACY-005)
// Named grievance officer publicly listed; contact details in persistent footer.

export default function GrievancesPage() {
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
        <h1 style={{ margin: 0, fontSize: "28px", fontWeight: 700 }}>
          Contact and Grievances
        </h1>
        <p
          style={{
            fontSize: "13px",
            color: "var(--fg-3)",
            marginTop: "8px",
          }}
        >
          Data Protection Grievance Officer details and contact information.
        </p>
      </header>

      <section style={sectionStyle}>
        <h2 style={h2style}>Grievance Officer</h2>
        <p style={pStyle}>
          Under the Digital Personal Data Protection Act, 2023, the platform
          designates the following individual as the Grievance Officer:
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
                <td style={tdLabel}>Name</td>
                <td style={tdValue}>Arvind Bhairat</td>
              </tr>
              <tr>
                <td style={tdLabel}>Role</td>
                <td style={tdValue}>
                  Grievance Officer (Phase A; designation reviewed at each phase
                  transition)
                </td>
              </tr>
              <tr>
                <td style={tdLabel}>Email</td>
                <td style={tdValue}>
                  <code style={codeStyle}>[GRIEVANCE_EMAIL]</code>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>How to Submit a Grievance</h2>
        <p style={pStyle}>
          If you have a concern or complaint regarding the processing of your
          personal data, please email the Grievance Officer from the email
          address registered on your account. Include a clear description of
          your concern and the right you wish to exercise under the DPDP Act.
        </p>
        <ul style={ulStyle}>
          <li>
            <strong>Acknowledgement:</strong> Within 7 calendar days of receipt.
          </li>
          <li>
            <strong>Resolution:</strong> Within 30 calendar days of receipt.
          </li>
        </ul>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>Your Rights</h2>
        <p style={pStyle}>
          As a data principal under the DPDP Act, you have the right to:
        </p>
        <ul style={ulStyle}>
          <li>
            <strong>Access</strong> — Request a copy of your stored personal
            data in machine-readable format.
          </li>
          <li>
            <strong>Correction</strong> — Request correction of inaccurate or
            incomplete personal data.
          </li>
          <li>
            <strong>Erasure</strong> — Request erasure of personal data, subject
            to lawful retention carve-outs.
          </li>
          <li>
            <strong>Grievance Redressal</strong> — Lodge a complaint with the
            Grievance Officer regarding data processing.
          </li>
        </ul>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>Escalation</h2>
        <p style={pStyle}>
          If you are not satisfied with the resolution provided by the Grievance
          Officer, you may escalate the matter to the Data Protection Board of
          India in accordance with the DPDP Act, 2023.
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
        <p>Signal Stack · Contact and Grievances</p>
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
