import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The console is served by the API under /admin (same origin as /admin/v1, so the SameSite=Strict
// session cookie with Path=/admin works). In development Vite proxies API calls to localhost:8080.
const api = process.env.KINDLING_API_URL ?? 'http://localhost:8080';

export default defineConfig({
  base: '/admin/',
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/admin/v1': { target: api, changeOrigin: false },
      '/v1': { target: api, changeOrigin: false },
      '/health': { target: api, changeOrigin: false },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    // The API serves this folder; keep the bundle reasonably small for phones.
    chunkSizeWarningLimit: 600,
  },
});
