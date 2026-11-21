import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// In development the API runs on 8484 and is proxied, so the session cookie stays on one origin
const server = process.env.AKIRA_SERVER ?? 'http://localhost:8484'

export default defineConfig({
    plugins: [vue(), tailwindcss()],
    resolve: {
        alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) }
    },
    server: {
        port: 5173,
        proxy: {
            '/api': { target: server, ws: true, changeOrigin: false }
        }
    },
    build: {
        outDir: '../Akira.Server/wwwroot',
        emptyOutDir: true
    }
})
