// REQ-SESSION-012 / REQ-SESSION-013: approval-pending screen.
// Rendered when the user has completed OAuth but their account is still awaiting
// admin approval.  Must not expose any protected portal content.
export default function PendingApprovalPage() {
  return (
    <main>
      <h1>Awaiting approval</h1>
      <p>
        Your account is pending admin approval. You will receive access once an
        administrator has reviewed and approved your request. Please check back
        later or contact your platform administrator for assistance.
      </p>
    </main>
  );
}
