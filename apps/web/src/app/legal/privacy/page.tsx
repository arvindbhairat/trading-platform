import type { CSSProperties } from "react";

// Privacy Policy — versioned document (REQ-LEGAL-008)
// Current version: v1 (Phase A)

export default function PrivacyPage() {
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
          Privacy Policy
        </h1>
      </header>

      <section style={sectionStyle}>
        <h2 style={h2style}>1. Data Controller</h2>
        <p style={pStyle}>
          Signal Stack operates as a data fiduciary under the Digital Personal
          Data Protection Act, 2023 (DPDP). The platform operator is the data
          fiduciary unless a separate operating entity is later established.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>2. Personal Data Collected</h2>
        <p style={pStyle}>
          We collect and process the following categories of personal data: (a)
          identity data — name, email address, and OAuth provider identifier; (b)
          FYERS account credentials and token data necessary for platform
          function; (c) usage data — signals configured, watchlists, portfolio
          analytics preferences; (d) communication data — support inquiries and
          correspondence; (e) Telegram account identifiers when you link your
          Telegram account for notifications.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>3. Purpose of Processing</h2>
        <p style={pStyle}>
          Personal data is processed solely for: (a) operating and maintaining
          the Platform; (b) delivering notifications through your linked Telegram
          account; (c) providing support and responding to inquiries; (d)
          complying with legal obligations; (e) improving the Platform. We do not
          sell, rent, or trade personal data. Processing for any new purpose
          requires updated consent.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>6. Data Retention</h2>
        <p style={pStyle}>
          Personal data is retained only as long as necessary for the purposes
          disclosed in this Policy or as required by law. Account data is
          retained for the duration of your account plus the retention periods
          specified in our Record of Processing Activities. Upon account
          deactivation or erasure request, personal identifiers are redacted
          while factual records (audit events, trade ledger) are retained as
          required by law.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>4. Data Sharing and Transfers</h2>
        <p style={pStyle}>
          Personal data is stored in Azure India regions (Central India or South
          India). Third-party processors — including FYERS, Telegram, and cloud
          infrastructure providers — may process data outside India where
          necessary for platform function. Cross-border transfers are documented
          in our Record of Processing Activities. By using the Platform, you
          consent to such processing in accordance with this Policy.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>5. Admin Access to User Data</h2>
        <p style={pStyle}>
          For operational support, troubleshooting, and platform diagnostics, the
          platform admin may access a read-only view of your account data. This
          includes your display name, trading data (positions, holdings, broker
          orders), platform-derived analytics (portfolio snapshot, equity curve,
          RME state), scan and subscription configuration, notification history,
          and settings. Financial credentials (FYERS token) are never transferred
          to the admin view.
        </p>
        <p style={{ ...pStyle, marginTop: "12px" }}>
          Each instance of admin access to your account is recorded in the audit
          log with a timestamp, the identity of the admin, and the purpose of
          access. You may request a copy of these audit records through the
          grievance officer.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>7. Your Rights</h2>
        <p style={pStyle}>
          Under the DPDP Act, you have the right to: (a) access your personal
          data; (b) request correction of inaccurate data; (c) request erasure
          of your personal data, subject to lawful retention carve-outs; (d)
          lodge a grievance with the designated grievance officer. To exercise
          these rights, contact the grievance officer through the details on our
          Contact and Grievances page.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>8. Grievance Officer</h2>
        <p style={pStyle}>
          During Phase A, the platform admin serves as the grievance officer.
          Contact: the platform admin through the Contact and Grievances page
          accessible from the portal footer. The grievance officer will
          acknowledge receipt of any grievance within 7 days and complete
          resolution within 30 days.
        </p>
      </section>

      <section style={sectionStyle}>
        <h2 style={h2style}>9. Changes to This Policy</h2>
        <p style={pStyle}>
          This Privacy Policy may be updated from time to time. When the version
          is bumped, you will be required to accept the updated Policy on your
          next login before accessing any protected Platform feature. Material
          changes will be communicated through the Platform.
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
        <p>Signal Stack · Privacy Policy v1</p>
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
