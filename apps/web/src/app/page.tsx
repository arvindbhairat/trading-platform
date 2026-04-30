import SessionExpiryBanner from "@/components/SessionExpiryBanner";

export default function Home() {
  return (
    <main>
      <SessionExpiryBanner />
      <h1>SignalStack</h1>
      <p>Portal baseline is up (v0.3).</p>
    </main>
  );
}
