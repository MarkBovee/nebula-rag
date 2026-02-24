import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

const devProxyOrigin = process.env.VITE_DEV_PROXY_ORIGIN || 'http://localhost:8099';
const devProxyBasePath = (process.env.VITE_DEV_PROXY_BASE_PATH || '').replace(/\/$/, '');

/// <summary>
/// Rewrites proxied API paths so local dev can target Home Assistant ingress base paths.
/// Example: /api/health with base '/nebula' -> /nebula/api/health.
/// </summary>
/// <param name="requestPath">Incoming Vite request path.</param>
/// <returns>Path with optional base prefix.</returns>
const withBasePath = (requestPath: string): string => {
  if (!devProxyBasePath) {
    return requestPath;
  }

  return `${devProxyBasePath}${requestPath}`;
};

export default defineConfig({
  base: './',
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src')
    }
  },
  build: {
    outDir: '../src/NebulaRAG.AddonHost/wwwroot/dashboard',
    emptyOutDir: true,
    sourcemap: false,
    minify: true,
    rollupOptions: {
      output: {
        entryFileNames: '[name].[hash].js',
        chunkFileNames: '[name].[hash].js',
        assetFileNames: '[name].[hash][extname]'
      }
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: devProxyOrigin,
        changeOrigin: true,
        rewrite: (requestPath) => withBasePath(requestPath)
      },
      '/mcp': {
        target: devProxyOrigin,
        changeOrigin: true,
        rewrite: (requestPath) => withBasePath(requestPath)
      }
    }
  }
});
