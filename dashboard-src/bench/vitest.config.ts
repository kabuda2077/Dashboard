import { defineConfig } from 'vitest/config'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  resolve: { alias: { '@': fileURLToPath(new URL('../src', import.meta.url)) } },
  test: {
    environment: 'happy-dom',
    include: ['bench/latency.bench.test.ts'],
    setupFiles: ['./src/__tests__/setup.ts'],
  },
})
