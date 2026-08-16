import '@/api/http'
import '@/helper/dayjs'
import 'tippy.js/animations/scale.css'
import 'tippy.js/dist/tippy.css'
import { createApp } from 'vue'
import App from './App.vue'
import './hostBootstrap'
import { loadFonts } from './assets/load-fonts'
import './assets/main.css'
import { installDashboardSettingsSync } from './helper/dashboardSettingsSync'
import { applyCustomThemes, applyKsuTheme } from './helper'
import { i18n } from './i18n'
import router from './router'

const appStartedAt = performance.now()
const isEdge = /Edg\//.test(navigator.userAgent)

if (isEdge) {
  const originalReplaceState = history.replaceState
  history.replaceState = function (...args) {
    if (document.visibilityState === 'hidden') return
    return originalReplaceState.apply(this, args)
  }
}

applyCustomThemes()
applyKsuTheme()
loadFonts()
installDashboardSettingsSync()

const app = createApp(App)

app.use(router)
app.use(i18n)
app.mount('#app')

window.requestAnimationFrame(() => {
  const webview = (window as { chrome?: { webview?: { postMessage?: (message: unknown) => void } } })
    .chrome?.webview
  webview?.postMessage?.({
    type: 'performance',
    name: 'frontendMounted',
    durationMs: Math.round(performance.now() - appStartedAt),
  })
})
