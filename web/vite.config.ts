import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'

const apiTarget = process.env.API_HTTPS ?? process.env.API_HTTP

export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      input: {
        app: fileURLToPath(new URL('./index.html', import.meta.url)),
        auth: fileURLToPath(new URL('./auth-callback.html', import.meta.url)),
      },
    },
  },
  server: {
    proxy: apiTarget
      ? {
          '/api': {
            target: apiTarget,
            changeOrigin: true,
          },
          '/health': {
            target: apiTarget,
            changeOrigin: true,
          },
          '/openapi': {
            target: apiTarget,
            changeOrigin: true,
          },
        }
      : undefined,
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
  },
})
