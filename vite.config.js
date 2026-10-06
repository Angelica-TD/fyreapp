import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'wwwroot/js/react',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        clientSearch: 'FyreFrontend/react/ClientSearch/index.jsx',
        taskSearch: 'FyreFrontend/react/TaskSearch/index.jsx'
      },
      output: {
        // Fixed entry names so Razor views can reference them; asp-append-version handles cache busting
        entryFileNames: 'assets/[name].js'
      }
    }
  }
})