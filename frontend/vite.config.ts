/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    // Local dev: forward API calls to the backend (docker compose uses nginx instead).
    proxy: { '/api': 'http://localhost:8080' },
  },
  test: { environment: 'jsdom' },
})
