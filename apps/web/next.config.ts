import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  reactCompiler: true,

  // OpenTelemetry packages are intentionally NOT in serverExternalPackages.
  // Turbopack externalizes them as symlinks in .next/node_modules/ with
  // hashed names (e.g. @opentelemetry/api-6ec0324a2d0bd38c). These symlinks
  // break in Docker multi-stage builds, causing runtime require() failures.
  // Bundling them via Turbopack works correctly for the Node.js server runtime.
};

export default nextConfig;

