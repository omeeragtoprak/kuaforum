/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  // web-admin ile deploy parity (Docker multi-stage, Caddy arkasında).
  output: 'standalone',
};

export default nextConfig;
