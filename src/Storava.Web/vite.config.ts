import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  base: '/dist/',
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./ClientApp', import.meta.url)),
    },
  },
  build: {
    outDir: 'wwwroot/dist',
    emptyOutDir: true,
    sourcemap: true,
    target: 'es2022',
    assetsInlineLimit: 0,
    rollupOptions: {
      input: {
        app: fileURLToPath(new URL('./ClientApp/styles/app.css', import.meta.url)),
        landing: fileURLToPath(new URL('./ClientApp/pages/landing.ts', import.meta.url)),
        privacy: fileURLToPath(new URL('./ClientApp/pages/privacy.ts', import.meta.url)),
        scan: fileURLToPath(new URL('./ClientApp/pages/scan.ts', import.meta.url)),
      },
      output: {
        entryFileNames: 'pages/[name].js',
        chunkFileNames: 'chunks/[name].js',
        assetFileNames: (assetInfo) =>
          assetInfo.names.some((name) => name.endsWith('.css'))
            ? 'assets/[name][extname]'
            : 'assets/[name]-[hash][extname]',
      },
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['ClientApp/**/*.test.ts'],
    setupFiles: ['ClientApp/test/setup.ts'],
    coverage: {
      reporter: ['text', 'json', 'html'],
    },
  },
});
