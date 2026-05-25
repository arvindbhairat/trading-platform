"use client";

// Admin layout: verifies the user has admin role before rendering admin pages.
// Redirects non-admin users to the dashboard.

import { useEffect, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { getToken } from "@/lib/auth";
import { useAuth } from "@/contexts/AuthContext";

export default function AdminLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { role, loading } = useAuth();

  useEffect(() => {
    // If no token at all, redirect to login.
    if (!loading && !getToken()) {
      router.replace("/login");
      return;
    }

    // If session loaded and user is not admin, redirect to dashboard.
    if (!loading && role !== null && role !== "admin") {
      router.replace("/");
    }
  }, [loading, role, router]);

  // Show nothing while checking auth.
  if (loading || role === null) {
    return null;
  }

  // Deny non-admin users (redundant with the effect above, but prevents flash).
  if (role !== "admin") {
    return null;
  }

  return <>{children}</>;
}
