import { capabilities } from '@/assembly/backend'
import { hasHostBridge } from '@/composables/hostBridge'
import { ROUTE_NAME } from '@/constant'
import { renderRoutes } from '@/helper'
import { i18n } from '@/i18n'
import { language } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import CorePage from '@/views/CorePage.vue'
import HomePage from '@/views/HomePage.vue'
import { useTitle } from '@vueuse/core'
import { watch } from 'vue'
import { createRouter, createWebHashHistory } from 'vue-router'
import { loadConnectionsPage, loadOverviewPage, loadProxiesPage } from './pageLoaders'

const childrenRouter = [
  {
    path: 'core',
    name: ROUTE_NAME.core,
    component: CorePage,
  },
  {
    path: 'proxies',
    name: ROUTE_NAME.proxies,
    component: loadProxiesPage,
  },
  {
    path: 'overview',
    name: ROUTE_NAME.overview,
    component: loadOverviewPage,
  },
  {
    path: 'connections',
    name: ROUTE_NAME.connections,
    component: loadConnectionsPage,
  },
  {
    path: 'logs',
    name: ROUTE_NAME.logs,
    component: () => import('@/views/LogsPage.vue'),
  },
  {
    path: 'rules',
    name: ROUTE_NAME.rules,
    component: () => import('@/views/RulesPage.vue'),
  },
]

const ROUTE_CAPABILITY: Partial<Record<string, keyof typeof capabilities.value>> = {
  [ROUTE_NAME.rules]: 'rules',
}

const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: ROUTE_NAME.proxies,
      component: HomePage,
      children: childrenRouter,
    },
    {
      path: '/setup',
      name: ROUTE_NAME.setup,
      component: () => import('@/views/SetupPage.vue'),
    },
    {
      path: '/:catchAll(.*)',
      redirect: ROUTE_NAME.proxies,
    },
  ],
})

const title = useTitle('Dashboard')
const setTitleByName = (name: string | symbol | undefined) => {
  if (typeof name === 'string' && activeBackend.value) {
    const backend = activeBackend.value
    const prefix = backend.label || `${backend.host}:${backend.port}`
    title.value = `${prefix} | ${i18n.global.t(name)}`
  } else {
    title.value = 'Dashboard'
  }
}

router.beforeEach((to, from) => {
  if (hasHostBridge && to.name === ROUTE_NAME.setup) return { name: ROUTE_NAME.core }
  const toIndex = renderRoutes.value.findIndex((item) => item === to.name)
  const fromIndex = renderRoutes.value.findIndex((item) => item === from.name)

  if (toIndex === 0 && fromIndex === renderRoutes.value.length - 1) {
    to.meta.transition = 'slide-left'
  } else if (toIndex === renderRoutes.value.length - 1 && fromIndex === 0) {
    to.meta.transition = 'slide-right'
  } else if (toIndex !== fromIndex) {
    to.meta.transition = toIndex < fromIndex ? 'slide-right' : 'slide-left'
  }

  if (!activeBackend.value && ![ROUTE_NAME.setup, ROUTE_NAME.core].includes(to.name as ROUTE_NAME)) {
    return { name: hasHostBridge ? ROUTE_NAME.core : ROUTE_NAME.setup }
  }

  const requiredCap = typeof to.name === 'string' ? ROUTE_CAPABILITY[to.name] : undefined
  if (requiredCap && !capabilities.value[requiredCap]) {
    router.push({ name: ROUTE_NAME.proxies })
  }
})

router.afterEach((to) => {
  setTitleByName(to.name)
})

watch([language, activeBackend], () => {
  setTimeout(() => {
    setTitleByName(router.currentRoute.value.name)
  })
})

watch(capabilities, (currentCapabilities) => {
  const routeName = router.currentRoute.value.name
  const requiredCap = typeof routeName === 'string' ? ROUTE_CAPABILITY[routeName] : undefined
  if (requiredCap && !currentCapabilities[requiredCap]) {
    router.push({ name: ROUTE_NAME.proxies })
  }
})

export default router
