import { NextResponse } from "next/server";
import { readFileSync } from "fs";
import { join } from "path";

export const dynamic = "force-dynamic";

export function GET() {
  try {
    const raw = readFileSync(
      join(process.cwd(), "public", "api-config.json"),
      "utf-8"
    );
    const { apiBaseUrl } = JSON.parse(raw) as { apiBaseUrl: string };
    console.log(`[api/config] apiBaseUrl=${apiBaseUrl || "(empty)"}`);
    return NextResponse.json({ apiBaseUrl });
  } catch (err) {
    console.log(`[api/config] failed to read config file: ${err}`);
    return NextResponse.json({ apiBaseUrl: "" });
  }
}
