import '@/api/http'
import '@/helper/dayjs'
import 'tippy.js/animations/scale.css'
import 'tippy.js/dist/tippy.css'
import { createApp } from 'vue'
import App from './App.vue'
import { loadFonts } from './assets/load-fonts'
import './assets/main.css'
import { applyCustomThemes } from './helper'
import { installDashboardSettingsSync } from './helper/dashboardSettingsSync'
import { reportStartupMark } from './helper/dashboardStartup'
import './hostBootstrap'
import { i18n } from './i18n'
import router from './router'

reportStartupMark('application-imports-ready', 'applicationImportsReady')
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
  reportStartupMark('frontend-mounted', 'frontendMounted')
})

void router.isReady().then(() => {
  // Wait for the initial route and give the browser a paint opportunity.
  // This marks DOM readiness; it does not measure physical screen presentation.
  window.requestAnimationFrame(() => {
    window.requestAnimationFrame(() => {
      reportStartupMark('frontend-ready', 'frontendReady')
    })
  })
})
