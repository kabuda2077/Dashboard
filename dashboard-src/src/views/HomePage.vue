<template>
  <div
    class="home-page bg-base-200 flex size-full overflow-hidden"
    :class="isSidebarCollapsed ? 'sidebar-collapsed' : 'sidebar-expanded'"
    :style="{ '--sidebar-width': isMiddleScreen ? '0px' : isSidebarCollapsed ? '4.5rem' : '16rem' }"
  >
    <div
      v-if="!isMiddleScreen"
      class="relative z-40 shrink-0 overflow-visible transition-[width] duration-320 ease-[cubic-bezier(0.34,0.1,0.2,1)]"
      style="width: var(--sidebar-width)"
    >
      <SideBar class="absolute inset-y-0 left-0" />
    </div>
    <main
      ref="swiperRef"
      class="relative min-w-0 flex-1 overflow-hidden"
    >
      <RouterView v-slot="{ Component, route }">
        <Transition
          :name="isMiddleScreen ? (route.meta.transition as string) || 'fade' : 'page'"
          :mode="isMiddleScreen ? undefined : 'out-in'"
        >
          <KeepAlive include="OverviewPage"><Component :is="Component" /></KeepAlive>
        </Transition>
      </RouterView>
      <div
        v-if="isMiddleScreen"
        ref="dockRef"
        class="dock dock-xs bg-base-100/20 z-10 h-14 shadow-sm backdrop-blur-sm"
        style="padding: 0; bottom: calc(var(--spacing) * 2 + env(safe-area-inset-bottom))"
      >
        <button
          v-for="name in renderRoutes"
          :key="name"
          class="h-14 flex-col items-center justify-center pt-2"
          :class="name === route.name && 'dock-active'"
          @click="router.push({ name, replace: true })"
        >
          <component
            :is="ROUTE_ICON_MAP[name]"
            class="h-5 w-5 shrink-0"
          /><span class="dock-label">{{ $t(name) }}</span>
        </button>
      </div>
    </main>
  </div>
</template>

<script setup lang="ts">
import SideBar from '@/components/sidebar/SideBar.vue'
import { dockTop } from '@/composables/paddingViews'
import { useSwipeRouter } from '@/composables/swipe'
import { ROUTE_ICON_MAP } from '@/constant/routeIcons'
import { renderRoutes } from '@/helper'
import { isMiddleScreen } from '@/helper/utils'
import { isSidebarCollapsed } from '@/store/settings'
import { useElementBounding } from '@vueuse/core'
import { onUnmounted, ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
const router = useRouter()
const route = useRoute()
const { swiperRef } = useSwipeRouter()
const dockRef = ref<HTMLDivElement>()
const { top } = useElementBounding(dockRef)
watch(
  top,
  () => {
    dockTop.value = window.innerHeight - top.value
  },
  { immediate: true },
)
onUnmounted(() => {
  dockTop.value = 0
})
</script>
