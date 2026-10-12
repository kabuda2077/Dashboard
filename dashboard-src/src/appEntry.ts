import '@/api/http'
import '@/helper/dayjs'
import 'tippy.js/animations/scale.css'
import 'tippy.js/dist/tippy.css'
import { createApp } from 'vue'
import App from './App.vue'
import { loadFonts } from './assets/load-fonts'
import './assets/main.css'
import { postHostMessage } from './composables/hostBridge'
import { applyCustomThemes } from './helper'
import { installDashboardSettingsSync } from './helper/dashboardSettingsSync'
import './hostBootstrap'
import { i18n } from './i18n'
import router from './router'

performance.mark('application-imports-ready')
applyCustomThemes()
loadFonts()
installDashboardSettingsSync()
window.addEventListener('pageshow', (event) => {
  if (event.persisted) installDashboardSettingsSync()
})

const app = createApp(App)

app.use(router)
app.use(i18n)
app.mount('#app')

window.requestAnimationFrame(() => {
  postHostMessage({
    type: 'performance',
    name: 'frontendMounted',
    durationMs: Math.round(performance.now()),
  })
})
