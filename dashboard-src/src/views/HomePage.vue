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
      <div ref="pageRef" class="absolute flex h-full w-full flex-col overflow-hidden">
        <RouterView v-slot="{ Component }">
          <Transition
            :name="pageTransitionName"
            :mode="pageTransitionMode"
            @before-leave="onPageBeforeLeave"
          >
            <Component :is="Component" />
          </Transition>
        </RouterView>
      </div>
      <nav
        v-if="isMiddleScreen"
        ref="dockRef"
        class="tab-bar absolute right-3 left-3 z-30"
        :style="{
          bottom: 'calc(var(--spacing) * 2 + env(safe-area-inset-bottom))',
          '--tab-count': renderRoutes.length,
          '--tab-index': Math.max(renderRoutes.indexOf(route.name as ROUTE_NAME), 0),
        }"
      >
        <span v-if="renderRoutes.includes(route.name as ROUTE_NAME)" class="tab-bar-indicator" aria-hidden="true" />
        <button
          v-for="name in renderRoutes"
          :key="name"
          class="tab-bar-item"
          :aria-current="name === route.name ? 'page' : undefined"
          :aria-label="$t(name)"
          @click="router.push({ name, replace: true })"
        >
          <component
            :is="ROUTE_ICON_MAP[name]"
            class="tab-bar-icon"
          /><span class="tab-bar-label">{{ $t(name) }}</span>
        </button>
      </nav>
    </main>
  </div>
</template>

<script setup lang="ts">
import SideBar from '@/components/sidebar/SideBar.vue'
import { dockTop } from '@/composables/use-padding-for-views'
import { useSwipeRouter } from '@/composables/use-swipe-router'
import { ROUTE_ICON_MAP } from '@/constant/routeIcons'
import { ROUTE_NAME } from '@/constant'
import { pageTransitionMode, pageTransitionName } from '@/helper/page-transition'
import { renderRoutes } from '@/helper'
import { isMiddleScreen } from '@/helper/utils'
import { isSidebarCollapsed } from '@/store/settings'
import { useElementBounding } from '@vueuse/core'
import { onUnmounted, ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
const router = useRouter()
const route = useRoute()
const { swiperRef, pageRef, onPageBeforeLeave } = useSwipeRouter()
const dockRef = ref<HTMLDivElement>()
const { top } = useElementBounding(dockRef)
watch(
  top,
  () => {
    dockTop.value = dockRef.value && isMiddleScreen.value ? window.innerHeight - top.value : 0
  },
  { immediate: true },
)
onUnmounted(() => {
  dockTop.value = 0
})
</script>

<style>
.tab-bar {
  display: flex;
  height: 3.875rem;
  padding: 0.25rem;
  border-radius: 9999px;
  background-color: color-mix(in oklab, var(--color-base-100) 20%, transparent);
  -webkit-backdrop-filter: blur(20px) saturate(180%);
  backdrop-filter: blur(20px) saturate(180%);
  border: 1px solid color-mix(in srgb, var(--color-base-content) 8%, transparent);
  box-shadow:
    0 4px 16px color-mix(in srgb, var(--color-base-content) 6%, transparent),
    inset 0 1px 0 color-mix(in srgb, white 35%, transparent),
    inset 0 0 0 1px color-mix(in srgb, white 6%, transparent);
}

.tab-bar-indicator {
  position: absolute;
  top: 0.25rem;
  bottom: 0.25rem;
  left: 0.25rem;
  width: calc((100% - 0.5rem) / var(--tab-count));
  border-radius: 9999px;
  background-color: color-mix(in srgb, var(--color-base-content) 9%, transparent);
  transform: translateX(calc(100% * var(--tab-index)));
  transition: transform 0.45s cubic-bezier(0.32, 0.72, 0, 1);
  pointer-events: none;
}

.tab-bar-item {
  position: relative;
  display: flex;
  min-width: 0;
  flex: 1 1 0;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.125rem;
  border-radius: 9999px;
  color: color-mix(in srgb, var(--color-base-content) 75%, transparent);
  outline: none;
  cursor: pointer;
  user-select: none;
  -webkit-tap-highlight-color: transparent;
  transition:
    color 0.2s ease,
    transform 0.2s cubic-bezier(0.32, 0.72, 0, 1);
}

.tab-bar-item:active {
  transform: scale(0.92);
}

.tab-bar-item[aria-current='page'] {
  color: var(--color-primary);
}

.tab-bar-item:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: -2px;
}

.tab-bar-icon {
  width: 1.3rem;
  height: 1.3rem;
  flex-shrink: 0;
}

.tab-bar-label {
  max-width: 100%;
  overflow: hidden;
  font-size: 0.625rem;
  font-weight: 500;
  line-height: 0.75rem;
  letter-spacing: 0.01em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tab-bar-item[aria-current='page'] .tab-bar-label {
  font-weight: 600;
}

@media (prefers-reduced-motion: reduce) {
  .tab-bar-indicator,
  .tab-bar-item {
    transition: none;
  }
}

.slide-right-enter-active,
.slide-right-leave-active,
.slide-left-enter-active,
.slide-left-leave-active {
  transition: transform var(--page-transition-duration) var(--page-transition-ease);
  position: absolute;
  left: 0;
  top: 0;
  width: 100%;
  height: 100%;
  will-change: transform;
  backface-visibility: hidden;
}

.slide-left-enter-from {
  transform: translateX(calc(100% + var(--swipe-offset, 0px)));
}
.slide-left-enter-to {
  transform: translateX(0);
}
.slide-left-leave-from {
  transform: translateX(var(--swipe-offset, 0px));
}
.slide-left-leave-to {
  transform: translateX(-100%);
}

.slide-right-enter-from {
  transform: translateX(calc(-100% + var(--swipe-offset, 0px)));
}
.slide-right-enter-to {
  transform: translateX(0);
}
.slide-right-leave-from {
  transform: translateX(var(--swipe-offset, 0px));
}
.slide-right-leave-to {
  transform: translateX(100%);
}

.page-enter-active,
.page-leave-active {
  transition: opacity 0.2s ease-in-out;
  will-change: opacity;
}
.page-enter-from,
.page-leave-to {
  opacity: 0;
}

.custom-background :is(.page-enter-active, .page-leave-active) {
  transition: none;
  will-change: auto;
}
.custom-background :is(.page-enter-from, .page-leave-to) {
  opacity: 1;
}

@media (prefers-reduced-motion: reduce) {
  .slide-right-enter-active,
  .slide-right-leave-active,
  .slide-left-enter-active,
  .slide-left-leave-active,
  .page-enter-active,
  .page-leave-active {
    transition-duration: 0.01ms;
  }
}
</style>
