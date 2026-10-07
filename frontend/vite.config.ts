import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    port: 5173,
  },
  optimizeDeps: {
    // maplibre-gl's internal worker script resolves relative to its own
    // module URL; Vite's dependency pre-bundling rewrites that path and
    // breaks it (the worker 404s and the map renders as a blank canvas with
    // no tiles or data layers). Excluding it from pre-bundling avoids that.
    exclude: ['maplibre-gl'],
  },
})
