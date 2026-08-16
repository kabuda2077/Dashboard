<template>
  <div
    ref="contentRef"
    class="w-full"
  >
    <div
      :id="itemId(SETTINGS_MENU_KEY.backend)"
      :data-key="SETTINGS_MENU_KEY.backend"
      class="mx-auto mb-4 w-full max-w-7xl md:mb-6"
    >
      <BackendSettings />
    </div>

    <div class="mx-auto w-full max-w-7xl px-2">
      <button
        class="hover:text-primary mt-1 mb-3 flex items-center gap-2 px-1 text-lg leading-7 font-semibold transition-colors focus:outline-none"
        :aria-expanded="settingsExpanded"
        type="button"
        @click="settingsExpanded = !settingsExpanded"
      >
        <span class="indicator">
          <span
            v-if="hasHostBridge && hostState.appUpdateAvailable"
            class="indicator-item top-1 -right-1 flex"
          >
            <span class="bg-secondary absolute h-2 w-2 animate-ping rounded-full"></span>
            <span class="bg-secondary h-2 w-2 rounded-full"></span>
          </span>
          <span>设置</span>
        </span>
        <ChevronDownIcon
          class="h-4 w-4 transition-transform"
          :class="settingsExpanded && 'rotate-180'"
        />
      </button>
    </div>

    <template v-if="settingsExpanded && isTwoColumns">
      <div
        class="grid w-full grid-cols-2 gap-8"
        :class="embedded ? '' : 'mx-auto max-w-7xl p-3'"
      >
        <div
          v-for="col in [0, 1]"
          :key="col"
          class="flex flex-col gap-3"
        >
          <div
            v-for="item in collapsibleItems.filter((_, i) => columnAssignment[i] === col)"
            :id="collapsibleItemId(item.key)"
            :key="item.key"
            :data-key="item.key"
            class="mb-4 rounded-lg p-2 md:mb-6"
          >
            <div
              v-if="item.key !== SETTINGS_MENU_KEY.general && item.key !== ABOUT_DASHBOARD_KEY"
              class="mt-1 mb-3 px-1 text-lg font-semibold"
            >
              {{ $t(item.label) }}
            </div>
            <component :is="item.component" />
          </div>
        </div>
      </div>
    </template>
    <div
      v-else-if="settingsExpanded"
      class="mx-auto w-full max-w-3xl space-y-1 md:space-y-2"
      :class="embedded ? '' : 'p-3 md:px-8 md:py-6'"
    >
      <div
        v-for="item in collapsibleItems"
        :id="collapsibleItemId(item.key)"
        :key="item.key"
        :data-key="item.key"
        class="mb-4 md:mb-6"
      >
        <div
          v-if="item.key !== SETTINGS_MENU_KEY.general && item.key !== ABOUT_DASHBOARD_KEY"
          class="mt-1 mb-3 px-1 text-lg font-semibold"
        >
          {{ $t(item.label) }}
        </div>
        <component :is="item.component" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import BackendSettings from '@/components/settings/backend/BackendSettings.vue'
import ConnectionsSettings from '@/components/settings/connections/ConnectionsSettings.vue'
import AboutDashboardSettings from '@/components/settings/general/AboutDashboardSettings.vue'
import ZashboardSettings from '@/components/settings/general/ZashboardSettings.vue'
import OverviewSettings from '@/components/settings/overview/OverviewSettings.vue'
import ProxiesSettings from '@/components/settings/proxies/ProxiesSettings.vue'
import { hasHostBridge, hostState } from '@/composables/hostBridge'
import { SETTINGS_MENU_KEY } from '@/constant'
import { ChevronDownIcon } from '@heroicons/vue/24/outline'
import { useElementSize } from '@vueuse/core'
import type { Component } from 'vue'
import { computed, nextTick, ref, watch } from 'vue'

type MenuItem = {
  key: string
  label: string
  component: Component
}

const ABOUT_DASHBOARD_KEY = 'about-dashboard'

const props = withDefaults(
  defineProps<{
    embedded?: boolean
    idPrefix?: string
    scrollTo?: string | null
  }>(),
  {
    embedded: false,
    idPrefix: 'settings-content',
    scrollTo: null,
  },
)

const contentRef = ref<HTMLDivElement>()
const { width } = useElementSize(contentRef)
const twoColumnsAvailable = computed(() => width.value >= 1000)
const isTwoColumns = computed(() => twoColumnsAvailable.value)

const menuItems = computed<MenuItem[]>(() => {
  return [
    ...(hasHostBridge
      ? [
          {
            key: ABOUT_DASHBOARD_KEY,
            label: '',
            component: AboutDashboardSettings,
          },
        ]
      : []),
    {
      key: SETTINGS_MENU_KEY.general,
      label: 'zashboardSettings',
      component: ZashboardSettings,
    },
    {
      key: SETTINGS_MENU_KEY.overview,
      label: 'overviewSettings',
      component: OverviewSettings,
    },
    {
      key: SETTINGS_MENU_KEY.proxies,
      label: 'proxySettings',
      component: ProxiesSettings,
    },
    {
      key: SETTINGS_MENU_KEY.connections,
      label: 'connectionSettings',
      component: ConnectionsSettings,
    },
  ]
})

const collapsibleItems = computed<MenuItem[]>(() => {
  return menuItems.value
})

const settingsExpanded = ref(false)
const columnAssignment = ref<number[]>(collapsibleItems.value.map((_, i) => i % 2))

const itemId = (key: string) => `${props.idPrefix}-${key}`
const collapsibleItemId = (key: string) => itemId(key)
const shouldExpandForKey = (key: string | null | undefined) => {
  return collapsibleItems.value.some((item) => item.key === key)
}

const rebalanceColumns = async () => {
  if (!settingsExpanded.value) {
    return
  }

  await nextTick()
  const colHeights = [0, 0]
  columnAssignment.value = collapsibleItems.value.map((item) => {
    const el = document.getElementById(collapsibleItemId(item.key))
    const h = el?.offsetHeight ?? 0
    const col = colHeights[0] <= colHeights[1] ? 0 : 1
    colHeights[col] += h
    return col
  })
}

watch(collapsibleItems, () => {
  columnAssignment.value = collapsibleItems.value.map((_, i) => i % 2)
  rebalanceColumns()
})

watch(isTwoColumns, rebalanceColumns, { immediate: true })

watch(settingsExpanded, rebalanceColumns)

watch(
  () => props.scrollTo,
  async (scrollTo) => {
    if (!scrollTo) {
      return
    }

    if (shouldExpandForKey(scrollTo)) {
      settingsExpanded.value = true
    }

    await nextTick()
    window.requestAnimationFrame(() => {
      document.getElementById(`${props.idPrefix}-${scrollTo}`)?.scrollIntoView({
        block: 'start',
        behavior: 'smooth',
      })
    })
  },
  { immediate: true },
)
</script>
