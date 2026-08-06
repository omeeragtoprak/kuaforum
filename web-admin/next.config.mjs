/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  output: 'standalone',
  // Next 15.5: typedRoutes experimental'dan stable'a taşındı (top-level).
  typedRoutes: true,
};

export default nextConfig;
