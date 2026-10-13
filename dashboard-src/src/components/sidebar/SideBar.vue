<template>
  <div
    ref="sidebarRef"
    class="sidebar border-base-300/30 bg-base-200 text-base-content scrollbar-hidden h-full overflow-x-hidden border-r p-2 transition-[width,padding] duration-320 ease-[cubic-bezier(0.34,0.1,0.2,1)]"
    :class="isSidebarCollapsed ? 'w-18 px-0' : 'w-64'"
    @transitionend="handleTransitionEnd"
  >
    <div :class="twMerge('flex h-full flex-col gap-2', isSidebarCollapsed ? 'w-18 px-0' : 'w-60')">
      <NavMenu
        ref="navMenuRef"
        class="sidebar-navigation flex-1"
        :items="navItems"
        :active-key="activeRouteName"
        :collapsed="isSidebarCollapsed"
      />
      <template v-if="isSidebarCollapsed">
        <VerticalInfos v-if="showStatisticsWhenSidebarCollapsed">
          <SidebarButtons vertical />
        </VerticalInfos>
        <SidebarButtons
          v-else
          vertical
        />
      </template>
      <template v-else>
        <OverviewCarousel />
        <CommonSidebar class="base-container" />
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import NavMenu, { type NavMenuItem } from '@/components/common/NavMenu.vue'
import CommonSidebar from '@/components/sidebar/CommonCtrl.vue'
import { ROUTE_ICON_MAP } from '@/constant/routeIcons'
import { renderRoutes } from '@/helper'
import { isSidebarCollapsed, showStatisticsWhenSidebarCollapsed } from '@/store/settings'
import { twMerge } from 'tailwind-merge'
import { computed, defineAsyncComponent, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
const OverviewCarousel = defineAsyncComponent(() => import('./OverviewCarousel.vue'))
import SidebarButtons from './SidebarButtons.vue'
import VerticalInfos from './VerticalInfos.vue'

const emit = defineEmits<{
  transitionend: []
}>()

const { t } = useI18n()
const route = useRoute()
const sidebarRef = ref<HTMLDivElement>()
const navMenuRef = ref<InstanceType<typeof NavMenu>>()

const activeRouteName = computed(() => (typeof route.name === 'string' ? route.name : undefined))
const navItems = computed<NavMenuItem[]>(() =>
  renderRoutes.value.map((r) => ({
    key: r,
    label: t(r),
    icon: ROUTE_ICON_MAP[r],
    to: { name: r },
  })),
)

const handleTransitionEnd = (e: TransitionEvent) => {
  if (e.target !== sidebarRef.value || e.propertyName !== 'width') return
  navMenuRef.value?.syncIndicator()
  emit('transitionend')
}
</script>

<style scoped>
.sidebar-navigation :deep(.nav-menu) {
  gap: 0.25rem;
  padding: 0.5rem;
}

.sidebar-navigation :deep(.nav-menu-collapsed) {
  align-items: stretch;
}

.sidebar-navigation :deep(.nav-item) {
  width: 100%;
  height: auto;
  min-height: 0;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius-field);
  font-size: 0.875rem;
  line-height: 1.5;
  font-weight: 400;
}

.sidebar-navigation :deep(.nav-item-icon) {
  width: 1.25rem;
  height: 1.25rem;
  color: inherit;
  stroke-width: 1.5;
}

.sidebar-navigation :deep(.nav-indicator) {
  border-radius: var(--radius-field);
  background-color: var(--color-neutral);
  box-shadow: none;
}

.sidebar-navigation :deep(.nav-item[aria-current='page']) {
  color: var(--color-neutral-content);
}

@media (hover: hover) {
  .sidebar-navigation :deep(.nav-item:not([aria-current='page']):hover) {
    background-color: var(--color-base-300);
  }
}
</style>
