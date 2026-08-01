/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    css: false,
  },
  server: {
    proxy: {
      // Dev proxy: /api -> backend (matches nginx /api/ proxy in production)
      '/api': {
        target: 'http://localhost:5189',
        changeOrigin: true,
      },
    },
  },
})
