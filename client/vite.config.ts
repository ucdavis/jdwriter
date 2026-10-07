import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { env } from 'node:process';
import { tanstackRouter } from '@tanstack/router-plugin/vite';

const target = env.ASPNETCORE_URLS
  ? env.ASPNETCORE_URLS.split(';')[0]
  : env.ASPNETCORE_HTTPS_PORT
    ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`
    : 'http://localhost:5165';

// https://vitejs.dev/config/
export default defineConfig(({ command }) => ({
  // Built assets are referenced relative to <base href>, which the server sets to the app's
  // mount point at request time — so one build runs at a host's root or under a path such as
  // /jdwriter. The dev server stays at the root.
  base: command === 'build' ? './' : '/',
  plugins: [
    tanstackRouter({
      autoCodeSplitting: true,
      target: 'react',
    }),
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    host: true,
    // Let the caller decide whether to open a browser. This avoids a second
    // tab when Visual Studio launches through ASP.NET Core SpaProxy.
    open: false,
    port: 5173,
    proxy: {
      '/health': {
        secure: false,
        target,
      },
      '/login': {
        secure: false,
        target,
      },
      '/logout': {
        secure: false,
        target,
      },
      '/signin-oidc': {
        secure: false,
        target,
      },
      '^/api': {
        secure: false,
        target,
      },
    },
  },
}));
