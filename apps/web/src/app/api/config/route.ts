import { NextResponse } from "next/server";
import { readFileSync } from "fs";
import { join } from "path";
import { serverLogger } from "@/lib/server-logger";

export const dynamic = "force-dynamic";

export function GET() {
  try {
    const raw = readFileSync(
      join(process.cwd(), "public", "api-config.json"),
      "utf-8"
    );
    const { apiBaseUrl } = JSON.parse(raw) as { apiBaseUrl: string };
    serverLogger.info("API config served", { apiBaseUrl: apiBaseUrl || "(empty)" });
    return NextResponse.json({ apiBaseUrl });
  } catch (err) {
    serverLogger.warn("Failed to read API config file", { error: String(err) });
    return NextResponse.json({ apiBaseUrl: "" });
  }
}
