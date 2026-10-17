import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Produces .next/standalone: a minimal server.js plus only the node_modules it needs,
  // so the Docker image doesn't have to ship the full node_modules folder.
  output: "standalone",
  // cacheComponents (and partialPrefetching, which builds on it) are off on purpose. With them on,
  // Next.js keeps a page alive while you're on another page and resumes it when you come back,
  // with the old selections and an in-flight state. This app is all client-side and gains nothing
  // from that; without it, leaving a page destroys it and coming back starts fresh.
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
