import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  reactCompiler: true,

  // No serverExternalPackages needed. The auto-instrumentation packages
  // (@opentelemetry/instrumentation, @opentelemetry/instrumentation-http)
  // that triggered Turbopack's hashed-symlink issue (require-in-the-middle
  // being symlinked to an absolute path that breaks in Docker) have been
  // removed — the web app doesn't need HTTP auto-instrumentation since
  // Next.js handles spans natively.
};

export default nextConfig;

