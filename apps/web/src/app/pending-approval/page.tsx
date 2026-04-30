// REQ-SESSION-012 / REQ-SESSION-013: approval-pending screen.
// Rendered when the user has completed OAuth but their account is still awaiting
// admin approval. Must not expose any protected portal content.
//
// Design system: uses tokens from globals.css and primitives.
// Layout matches design_system/mock_screens/auth-screens.jsx.

import { Card, Logo, Icon } from "@/components/primitives";

export default function PendingApprovalPage() {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        minHeight: "100vh",
        background: "var(--bg-0)",
        padding: "var(--s-6)",
      }}
    >
      <Card
        style={{
          maxWidth: "440px",
          width: "100%",
          padding: "var(--s-8) var(--s-8) var(--s-6)",
          textAlign: "center",
        }}
      >
        <div style={{ marginBottom: "var(--s-6)" }}>
          <Logo size={40} />
        </div>
        <div
          style={{
            width: "48px",
            height: "48px",
            borderRadius: "50%",
            background: "var(--warn-bg)",
            color: "var(--warn-500)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            margin: "0 auto var(--s-4)",
          }}
        >
          <Icon name="clock" size={24} />
        </div>
        <h1 style={{ marginBottom: "var(--s-3)" }}>Awaiting approval</h1>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Your account is pending admin approval. You will receive access once an
          administrator has reviewed and approved your request. Please check back
          later or contact your platform administrator for assistance.
        </p>
      </Card>
    </div>
  );
}
