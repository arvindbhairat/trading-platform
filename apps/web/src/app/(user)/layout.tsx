"use client";

// Auth guard layout for all user-facing pages (chart, signals, notifications, backtest).
// Redirects to /login if no JWT token is present.

import { useEffect, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { getToken } from "@/lib/auth";

export default function UserLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [authChecked] = useState(() => !!getToken());

  useEffect(() => {
    if (!authChecked) {
      router.replace("/login");
    }
  }, [authChecked, router]);

  if (!authChecked) {
    return null;
  }

  return <>{children}</>;
}
