"use client";

import { useEffect } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { storeToken } from "@/lib/auth";

// Receives the JWT from the API OAuth callback redirect and stores it in
// sessionStorage so the portal can attach it as Bearer on API calls.
export default function AuthCallbackPage() {
  const router = useRouter();
  const params = useSearchParams();

  useEffect(() => {
    const token = params.get("token");
    const error = params.get("error");

    if (error || !token) {
      router.replace("/login?error=auth_failed");
      return;
    }

    storeToken(token);
    router.replace("/");
  }, [params, router]);

  return (
    <main>
      <p>Completing sign-in…</p>
    </main>
  );
}
