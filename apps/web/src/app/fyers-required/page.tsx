// REQ-SESSION-010 / REQ-SESSION-011 / REQ-SESSION-013: FYERS connection required screen.
// Rendered when the user has completed portal OAuth but has no valid FYERS token on
// record.  No protected platform feature is accessible until FYERS auth is complete.
export default function FyersRequiredPage() {
  return (
    <main>
      <h1>FYERS authentication required</h1>
      <p>
        To access SignalStack you must connect your FYERS brokerage account.
        FYERS authentication is required before you can use the dashboard,
        charts, portfolio analytics, or any other platform feature.
      </p>
      <p>
        Please complete FYERS authentication to continue. If you do not have a
        FYERS account, contact your platform administrator.
      </p>
    </main>
  );
}
