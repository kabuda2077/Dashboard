import { defineConfig } from 'vitest/config'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  resolve: { alias: { '@': fileURLToPath(new URL('../src', import.meta.url)) } },
  test: {
    environment: 'happy-dom',
    include: ['bench/*.bench.test.ts'],
    testTimeout: 30000,
    setupFiles: ['./src/__tests__/setup.ts'],
  },
})
