<template>
  <div
    class="home-page flex size-full bg-base-200"
    :class="sidebarLayoutCollapsed ? 'sidebar-collapsed' : 'sidebar-expanded'"
  >
    <div
      v-if="!isMiddleScreen"
      class="relative z-40 flex-none overflow-visible transition-[width] duration-320 ease-[cubic-bezier(0.34,0.1,0.2,1)]"
      :class="sidebarLayoutCollapsed ? 'w-18' : 'w-64'"
    >
      <SideBar class="absolute inset-y-0 left-0" />
    </div>
    <RouterView v-slot="{ Component, route }">
      <div
        class="relative flex-1 overflow-hidden"
        ref="swiperRef"
      >
        <div class="absolute flex h-full w-full flex-col overflow-hidden">
          <div
            class="relative min-h-0 flex-1 overflow-hidden"
          >
            <Transition
              :name="(route.meta.transition as string) || 'fade'"
              v-if="isMiddleScreen"
            >
              <Component :is="Component" />
            </Transition>
            <Transition
              v-else
              name="page"
              mode="out-in"
            >
              <Component :is="Component" />
            </Transition>
          </div>
        </div>

        <template v-if="isMiddleScreen">
          <div
            class="bg-base-100/20 dock dock-xs z-10 h-14 w-auto shadow-sm backdrop-blur-sm"
            :style="{
              padding: '0',
              bottom: 'calc(var(--spacing) * 2 + env(safe-area-inset-bottom))',
            }"
            ref="dockRef"
          >
            <button
              v-for="r in renderRoutes"
              :key="r"
              @click="router.push({ name: r, replace: true })"
              class="h-14 flex-col items-center justify-center pt-2"
              :class="r === route.name && 'dock-active'"
            >
              <component
                :is="ROUTE_ICON_MAP[r]"
                class="h-5 w-5 flex-shrink-0"
              />
              <span class="dock-label">
                {{ $t(r) }}
              </span>
            </button>
          </div>
          <div
            class="fixed bottom-0 z-10 w-full"
            style="
              background: linear-gradient(
                to top,
                rgba(0, 0, 0, 0.18) 0%,
                rgba(0, 0, 0, 0.1) 30%,
                rgba(0, 0, 0, 0.04) 60%,
                rgba(0, 0, 0, 0.01) 85%,
                rgba(0, 0, 0, 0) 100%
              );
              height: env(safe-area-inset-bottom);
            "
          ></div>
        </template>
      </div>
    </RouterView>

  </div>
</template>

<script setup lang="ts">
import SideBar from '@/components/sidebar/SideBar.vue'
import { dockTop } from '@/composables/paddingViews'
import { useSwipeRouter } from '@/composables/swipe'
import { PROXY_TAB_TYPE, ROUTE_ICON_MAP, ROUTE_NAME, RULE_TAB_TYPE } from '@/constant'
import { renderRoutes } from '@/helper'
import { isMiddleScreen } from '@/helper/utils'
import { fetchConfigs } from '@/store/config'
import { initConnections } from '@/store/connections'
import { initLogs } from '@/store/logs'
import { initSatistic } from '@/store/overview'
import { fetchProxies, proxiesTabShow } from '@/store/proxies'
import { fetchRules, rulesTabShow } from '@/store/rules'
import { isSidebarCollapsed } from '@/store/settings'
import { activeUuid } from '@/store/setup'
import { useDocumentVisibility, useElementBounding } from '@vueuse/core'
import { ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'

const router = useRouter()
const route = useRoute()
const { swiperRef } = useSwipeRouter()
const sidebarLayoutCollapsed = ref(isSidebarCollapsed.value)
const initializedTasks = new Set<string>()

const dockRef = ref<HTMLDivElement>()
const { top: dockRefTop } = useElementBounding(dockRef)

watch(isSidebarCollapsed, (value) => {
  sidebarLayoutCollapsed.value = value
})

watch(
  isMiddleScreen,
  (value) => {
    if (!value) {
      sidebarLayoutCollapsed.value = isSidebarCollapsed.value
    }
  },
  { immediate: true },
)

watch(
  dockRefTop,
  () => {
    dockTop.value = window.innerHeight - dockRefTop.value
  },
  { immediate: true },
)

const ensureTask = (key: string, task: () => void) => {
  if (initializedTasks.has(key)) return
  initializedTasks.add(key)
  task()
}

const initializeGlobalData = () => {
  ensureTask('connections', initConnections)
  ensureTask('proxies', fetchProxies)
  ensureTask('statistics', initSatistic)
}

const initializeRouteData = () => {
  if (!activeUuid.value) return

  const routeName = route.name
  if (!routeName || routeName === ROUTE_NAME.core) {
    return
  }

  ensureTask('configs', fetchConfigs)

  switch (routeName) {
    case ROUTE_NAME.rules:
      ensureTask('rules', fetchRules)
      break
    case ROUTE_NAME.logs:
      ensureTask('logs', initLogs)
      break
  }
}

watch(
  activeUuid,
  () => {
    if (!activeUuid.value) return
    initializedTasks.clear()
    rulesTabShow.value = RULE_TAB_TYPE.RULES
    proxiesTabShow.value = PROXY_TAB_TYPE.PROXIES
    initializeGlobalData()
    initializeRouteData()
  },
  {
    immediate: true,
  },
)

watch(
  () => route.name,
  initializeRouteData,
  {
    immediate: true,
  },
)

const documentVisible = useDocumentVisibility()

watch(documentVisible, () => {
  if (documentVisible.value !== 'visible') return
  if (initializedTasks.has('proxies')) {
    fetchProxies()
  }
})
</script>
