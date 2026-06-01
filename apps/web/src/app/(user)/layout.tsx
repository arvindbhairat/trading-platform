"use client";

// User route-group layout: provides Shell chrome (TopBar + SideNav + LegalFooter)
// and auth guard for all (user) pages. Navigation is wired centrally here.

import { useEffect, useState, useCallback, type ReactNode } from "react";
import { useRouter, usePathname } from "next/navigation";
import { getToken } from "@/lib/auth";
import { apiFetch } from "@/lib/auth";
import { getLiveAlerts } from "@/lib/live-alerts";
import {
  Shell,
  userNavItems,
  userNavRoutes,
  resolveActiveNavId,
  userNavItemsWithNotificationCount,
} from "@/components/primitives";

export default function UserLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();

  const [hasToken] = useState(() => !!getToken());
  const [unreadTotal, setUnreadTotal] = useState(0);
  const [unreadCritical, setUnreadCritical] = useState(0);

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
  const navItems = userNavItemsWithNotificationCount(unreadTotal, unreadCritical);

  const handleNavigate = (id: string) => {
    const route = userNavRoutes[id];
    if (route) router.push(route);
  };

  return (
    <Shell current={current} navItems={navItems} onNavigate={handleNavigate}>
      {children}
    </Shell>
  );
}
