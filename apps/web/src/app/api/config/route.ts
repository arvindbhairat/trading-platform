import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

export function GET() {
  return NextResponse.json({
    apiBaseUrl: process.env.API_BASE_URL ?? "",
  });
}
