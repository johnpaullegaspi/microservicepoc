import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server proxies /api to the gateway so the browser never needs a hard-coded service URL.
const target = process.env.VITE_PROXY_TARGET ?? 'http://localhost:8080';
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': { target, changeOrigin: true } } },
});
