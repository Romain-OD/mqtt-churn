import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // The watcher serves the built app, so the page and /frames share one origin: no CORS.
  build: { outDir: '../src/MqttChurn.Watch/wwwroot', emptyOutDir: true },
  // `npm run dev`: hot reload for the UI, /frames forwarded to a running watcher.
  server: { proxy: { '/frames': 'http://localhost:5080' } },
})
