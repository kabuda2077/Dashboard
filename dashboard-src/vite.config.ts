import vue from '@vitejs/plugin-vue'
import vueJsx from '@vitejs/plugin-vue-jsx'
import { execSync } from 'child_process'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import { VitePWA } from 'vite-plugin-pwa'
import { version } from './package.json' with { type: 'json' }

const getGitCommitId = (): string => {
  try {
    const commitMessage = execSync('git log -1 --pretty=%B', { encoding: 'utf8' }).trim()

    if (commitMessage.includes('chore(main): release')) {
      return ''
    }

    return execSync('git rev-parse --short HEAD', { encoding: 'utf8' }).trim()
  } catch (error) {
    console.warn('无法获取git commit ID:', error)
    return ''
  }
}

const font = process.env.FONT || 'all'
const desktopBuild = process.env.DESKTOP_BUILD === '1'

const fixMiSansVariableFontWeight = () => ({
  name: 'fix-misans-variable-font-weight',
  enforce: 'pre' as const,
  transform(code: string, id: string) {
    if (!id.includes('MiSans-VF.css')) {
      return null
    }

    const normalizedId = id.replace(/\\/g, '/')

    if (!normalizedId.endsWith('/subsetted-fonts/MiSans-VF/MiSans-VF.css')) {
      return null
    }

    if (!code.includes('font-weight: undefined')) {
      return null
    }

    return code.replace(/font-weight:\s*undefined/g, 'font-weight: 100 900')
  },
})

// https://vite.dev/config/
export default defineConfig({
  define: {
    __APP_VERSION__: JSON.stringify(version),
    __COMMIT_ID__: JSON.stringify(getGitCommitId()),
    __FONT__: JSON.stringify(font),
  },
  base: './',
  plugins: [
    fixMiSansVariableFontWeight(),
    vue(),
    vueJsx(),
    ...(
      desktopBuild
        ? []
        : [
            VitePWA({
              registerType: 'autoUpdate',
              includeAssets: ['favicon.svg', 'favicon-dark.svg'],
              workbox: {
                maximumFileSizeToCacheInBytes: 2 * 1024 * 1024,
              },
              manifest: {
                name: 'zashboard',
                short_name: 'zashboard',
                description: 'a dashboard using clash api',
                theme_color: '#000000',
                icons: [
                  {
                    src: './pwa-192x192.png',
                    sizes: '192x192',
                    type: 'image/png',
                    purpose: 'any',
                  },
                  {
                    src: './pwa-512x512.png',
                    sizes: '512x512',
                    type: 'image/png',
                    purpose: 'any',
                  },
                  {
                    src: './pwa-maskable-192x192.png',
                    sizes: '192x192',
                    type: 'image/png',
                    purpose: 'maskable',
                  },
                  {
                    src: './pwa-maskable-512x512.png',
                    sizes: '512x512',
                    type: 'image/png',
                    purpose: 'maskable',
                  },
                ],
              },
            }),
          ]
    ),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      net: fileURLToPath(new URL('./src/shims/net.ts', import.meta.url)),
      'node:net': fileURLToPath(new URL('./src/shims/net.ts', import.meta.url)),
    },
  },
  build: {
    target: 'es2020',
    minify: 'terser',
    terserOptions: {
      compress: {
        drop_console: true,
        drop_debugger: true,
        pure_funcs: ['console.log', 'console.info', 'console.debug'],
      },
      format: {
        comments: false,
      },
    },
    rollupOptions: {
      onwarn(warning, warn) {
        const id = warning.id?.replace(/\\/g, '/') || ''
        if (
          warning.code === 'INVALID_ANNOTATION' &&
          id.includes('/@vueuse/core/') &&
          warning.message.includes('#__PURE__')
        ) {
          return
        }

        warn(warning)
      },
      output: {
        manualChunks(id) {
          if (id.includes('node_modules')) {
            if (id.includes('vue') || id.includes('vue-router') || id.includes('vue-i18n')) {
              return 'vue-vendor'
            }
            if (id.includes('echarts')) {
              return 'charts-vendor'
            }
            if (id.includes('@heroicons')) {
              return 'icons-vendor'
            }
            if (id.includes('tippy')) {
              return 'tooltip-vendor'
            }
            if (id.includes('axios') || id.includes('dayjs') || id.includes('lodash') || id.includes('dompurify')) {
              return 'utils-vendor'
            }
            if (id.includes('@tanstack')) {
              return 'table-vendor'
            }
          }
        },
        chunkFileNames: 'assets/[name]-[hash].js',
        entryFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]',
      },
    },
    chunkSizeWarningLimit: 1000,
    reportCompressedSize: false,
    cssCodeSplit: true,
  },
  optimizeDeps: {
    include: ['vue', 'vue-router', 'vue-i18n', 'echarts', 'axios', 'dayjs'],
  },
  test: {
    environment: 'happy-dom',
    globals: true,
    include: ['src/**/__tests__/**/*.test.ts'],
    setupFiles: ['./src/__tests__/setup.ts'],
    restoreMocks: true,
  },
})
