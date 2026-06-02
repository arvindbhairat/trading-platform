import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  reactCompiler: true,

  // Externalize OpenTelemetry instrumentation packages from the Turbopack
  // server bundle. Without this, Turbopack creates hashed symlinks in
  // .next/node_modules/ (e.g. require-in-the-middle-<hash>) that point to
  // absolute paths on the build machine. These symlinks break in Docker/
  // Railway deployments where the build path does not exist, causing:
  //   "Cannot find module 'require-in-the-middle-<hash>'"
  // at startup when the instrumentation hook fires.
  //
  // Externalizing keeps them as plain require() calls that resolve from
  // node_modules/ at runtime — which is guaranteed to exist in the deployed
  // container (it was installed during the build stage).
  serverExternalPackages: [
    "@opentelemetry/instrumentation",
    "@opentelemetry/instrumentation-http",
    "require-in-the-middle",
    "import-in-the-middle",
  ],
};

export default nextConfig;

