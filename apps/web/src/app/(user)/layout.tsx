"use client";

// Auth guard layout for all user-facing pages (chart, signals, notifications, backtest).
// Redirects to /login if no JWT token is present.

import { useEffect, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { getToken } from "@/lib/auth";

export default function UserLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [authChecked, setAuthChecked] = useState(false);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    setAuthChecked(true);
  }, [router]);

  if (!authChecked) {
    return null;
  }

  return <>{children}</>;
}
