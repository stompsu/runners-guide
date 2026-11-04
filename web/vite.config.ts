import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const apiTarget = process.env.API_HTTPS ?? process.env.API_HTTP

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: apiTarget
      ? {
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
