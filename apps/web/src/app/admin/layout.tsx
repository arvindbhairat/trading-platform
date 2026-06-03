"use client";

// Admin route-group layout: provides Shell chrome (TopBar + SideNav + LegalFooter)
// with admin-specific nav items and role guard. Navigation is wired centrally here.

import { useEffect, useState, useCallback, type ReactNode } from "react";
import { useRouter, usePathname } from "next/navigation";
import { getToken } from "@/lib/auth";
import { useAuth } from "@/contexts/AuthContext";
import { apiFetch } from "@/lib/auth";
import {
  Shell,
  adminNavItems,
  adminNavRoutes,
  resolveActiveNavId,
  adminNavItemsWithApprovals,
} from "@/components/primitives";

export default function AdminLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { role, loading, userName } = useAuth();

  const [pendingApprovalCount, setPendingApprovalCount] = useState(0);

  // Auth guard: redirect non-admin users.
  useEffect(() => {
    if (!loading && !getToken()) {
      router.replace("/login");
      return;
    }
    if (!loading && role !== null && role !== "admin") {
      router.replace("/");
    }
  }, [loading, role, router]);

  // Fetch pending approvals count for the sidebar badge.
  const fetchApprovalCount = useCallback(async () => {
    try {
      const res = await apiFetch("/api/v1/admin/users/pending/count");
      if (res.ok) {
        const data = await res.json();
        setPendingApprovalCount(data.count ?? 0);
      }
    } catch {
      // Silently fail — badge shows stale data.
    }
  }, []);

  useEffect(() => {
    const token = getToken();
    if (!token || role !== "admin") return;
    fetchApprovalCount();
  }, [role, fetchApprovalCount]);

  // Show nothing while checking auth.
  if (loading || role === null) {
    return null;
  }

  // Deny non-admin users.
  if (role !== "admin") {
    return null;
  }

  const current = resolveActiveNavId(pathname, adminNavRoutes) ?? "home";
  const navItems = adminNavItemsWithApprovals(pendingApprovalCount);

  const handleNavigate = (id: string) => {
    const route = adminNavRoutes[id];
    if (route) router.push(route);
  };

  // Compute user initials from the OAuth display name for the profile circle.
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
      admin={true}
      userInitials={initials}
    >
      {children}
    </Shell>
  );
}
