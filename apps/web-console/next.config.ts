import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  // A self-contained server for running as a Windows service behind IIS (spec §13).
  output: 'standalone',
  poweredByHeader: false,
  experimental: {
    // Photo uploads (up to 10 MB) pass through the proxy and a server action.
    proxyClientMaxBodySize: '12mb',
    serverActions: { bodySizeLimit: '12mb' },
  },
};

export default nextConfig;
