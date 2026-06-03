"use client";

// User route-group layout: provides Shell chrome (TopBar + SideNav + LegalFooter)
// and auth guard for all (user) pages. Navigation is wired centrally here.

import { useEffect, useState, useCallback, type ReactNode } from "react";
import { useRouter, usePathname } from "next/navigation";
import { getToken } from "@/lib/auth";
import { apiFetch } from "@/lib/auth";
import { useAuth } from "@/contexts/AuthContext";
import { getLiveAlerts } from "@/lib/live-alerts";
import {
  Shell,
  userNavItems,
  userNavRoutes,
  resolveActiveNavId,
  userNavItemsWithNotificationCount,
  type NavItem,
} from "@/components/primitives";

export default function UserLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();

  const [hasToken] = useState(() => !!getToken());
  const [unreadTotal, setUnreadTotal] = useState(0);
  const [unreadCritical, setUnreadCritical] = useState(0);
  const { role, userName, fyersUserId } = useAuth();

  // Auth guard: redirect to login if no JWT token.
  useEffect(() => {
    if (!hasToken) {
      router.replace("/login");
    }
  }, [hasToken, router]);

  // Fetch unread notification count for the sidebar badge.
  const fetchUnreadCount = useCallback(async () => {
    try {
      const res = await apiFetch("/api/v1/notifications/unread-count");
      if (res.ok) {
        const data = await res.json();
        setUnreadTotal(data.total_unread ?? 0);
        setUnreadCritical(data.critical_unread ?? 0);
      }
    } catch {
      // Silently fail — badge shows stale data.
    }
  }, []);

  useEffect(() => {
    if (!hasToken) return;
    fetchUnreadCount();
  }, [hasToken, fetchUnreadCount]);

  // Subscribe to push events to refresh badge count in real time.
  // The LiveAlertsClient singleton is already started by PushAlertProvider
  // at the AppProviders level — calling start() here would kill that existing
  // WebSocket and create a perpetual 10s reconnect cycle (REQ-NFR-013).
  useEffect(() => {
    if (!hasToken) return;
    const client = getLiveAlerts();
    const unsub = client.onPushEvent(() => {
      fetchUnreadCount();
    });
    return () => {
      unsub();
    };
  }, [hasToken, fetchUnreadCount]);

  if (!hasToken) {
    return null;
  }

  const current = resolveActiveNavId(pathname, userNavRoutes) ?? "dashboard";
  const baseNavItems = userNavItemsWithNotificationCount(unreadTotal, unreadCritical);
  const navItems: NavItem[] =
    role === "admin"
      ? [
          ...baseNavItems,
          {
            id: "admin",
            label: "Admin Panel",
            icon: "shield",
          },
        ]
      : baseNavItems;

  const handleNavigate = (id: string) => {
    if (id === "admin") {
      router.push("/admin");
      return;
    }
    const route = userNavRoutes[id];
    if (route) router.push(route);
  };

  // Compute user initials from the OAuth display name for the profile circle.
  // Takes the first letter of the first and last name (e.g. "John Doe" → "JD").
  const initials = userName
    ? userName
        .split(" ")
        .map((n) => n[0])
        .join("")
        .toUpperCase()
        .slice(0, 2)
    : undefined;

  return (
    <Shell
      current={current}
      navItems={navItems}
      onNavigate={handleNavigate}
      userInitials={initials}
      fyersUserId={fyersUserId}
    >
      {children}
    </Shell>
  );
}
