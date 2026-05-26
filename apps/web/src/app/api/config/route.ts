import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

export function GET() {
  const apiBaseUrl = process.env.API_BASE_URL ?? "";
  console.log(`[api/config] apiBaseUrl=${apiBaseUrl || "(empty)"}`);
  return NextResponse.json({ apiBaseUrl });
}
