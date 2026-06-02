import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  reactCompiler: true,

  // Use the Railway git commit SHA as the Next.js build ID so that every
  // deploy produces a different _next/static/<buildId>/ path. This busts
  // browser caches for all JS/CSS bundles automatically — no stale-asset
  // problems after rolling out a new image.
  generateBuildId: async () => {
    const sha =
      process.env["RAILWAY_GIT_COMMIT_SHA"]?.trim() ||
      process.env["BUILD_ID"]?.trim();
    if (sha) return sha;
    // Fallback: let Next.js use its default random build ID (local dev).
    return null;
  },

  // No serverExternalPackages needed. The auto-instrumentation packages
  // (@opentelemetry/instrumentation, @opentelemetry/instrumentation-http)
  // that triggered Turbopack's hashed-symlink issue (require-in-the-middle
  // being symlinked to an absolute path that breaks in Docker) have been
  // removed — the web app doesn't need HTTP auto-instrumentation since
  // Next.js handles spans natively.
};

export default nextConfig;

