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
      }
    }
  }
})