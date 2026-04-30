// REQ-AUTH-001 / REQ-AUTH-011: always show all three OAuth providers.
// The buttons navigate directly to the backend OAuth redirect endpoints.
// Provider buttons must not be hidden or pre-screened based on availability.
const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";

const providers = [
  {
    name: "Google",
    key: "google",
    label: "Sign in with Google",
  },
  {
    name: "Microsoft",
    key: "microsoft",
    label: "Sign in with Microsoft",
  },
  {
    name: "Facebook",
    key: "facebook",
    label: "Sign in with Facebook / Meta",
  },
] as const;

export default function LoginPage() {
  return (
    <main>
      <h1>Sign in to SignalStack</h1>
      <p>Choose a sign-in method to continue.</p>
      <ul>
        {providers.map((p) => (
          <li key={p.key}>
            <a href={`${API_BASE}/api/v1/auth/login/${p.key}`}>
              {p.label}
            </a>
          </li>
        ))}
      </ul>
    </main>
  );
}
