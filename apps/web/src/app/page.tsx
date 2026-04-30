import SessionExpiryBanner from "@/components/SessionExpiryBanner";
import { Shell, Card, Pill, userNavItems } from "@/components/primitives";

export default function Home() {
  return (
    <Shell current="dashboard" navItems={userNavItems}>
      <div style={{ padding: "var(--s-8) var(--s-10)" }}>
        <SessionExpiryBanner />

        <h1 style={{ marginBottom: "var(--s-2)" }}>Dashboard</h1>
        <p className="t-body" style={{ marginBottom: "var(--s-6)" }}>
          Portal baseline is up (v0.3).
        </p>

        <Card
          style={{
            padding: "var(--s-8)",
            textAlign: "center",
            maxWidth: "480px",
          }}
        >
          <Pill tone="up" dot>
            All systems normal
          </Pill>
          <p className="t-body" style={{ marginTop: "var(--s-4)", marginBottom: 0 }}>
            Welcome to Signal Stack. Dashboard widgets, charts, and portfolio
            analytics will appear here as they are implemented.
          </p>
        </Card>
      </div>
    </Shell>
  );
}
