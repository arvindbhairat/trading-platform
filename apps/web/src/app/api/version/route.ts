import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

export function GET() {
  return NextResponse.json({
    buildId: process.env.RAILWAY_GIT_COMMIT_SHA || null,
  });
}
