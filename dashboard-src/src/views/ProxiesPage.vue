<template>
  <div
    class="flex h-full w-full flex-col"
    :style="padding"
    :class="[disableProxiesPageTextSelect ? 'select-none' : '']"
  >
    <ProxiesCtrl />
    <div class="relative flex min-h-0 w-full flex-1">
      <FolderManagerPanel v-if="foldersUiVisible && folderManagerOpen" />
      <div
        class="max-md:scrollbar-hidden relative h-full min-w-0 flex-1"
        :class="disableProxiesPageScroll ? 'overflow-y-hidden' : 'overflow-y-scroll'"
        :id="PROXIES_PAGE"
        ref="proxiesRef"
        @scroll.passive="handleScroll"
      >
        <FolderTopBar v-if="foldersUiVisible" />
        <template v-if="displayTwoColumns">
          <div class="grid grid-cols-2 gap-3 p-3">
            <div
              v-for="idx in [0, 1]"
              :key="idx"
              class="flex flex-1 flex-col gap-3"
            >
              <component
                v-for="name in filterContent(renderPageItems, idx)"
                :is="renderComponent"
                :key="name"
                :name="name"
              />
            </div>
          </div>
        </template>
        <div
          class="grid grid-cols-1 gap-3 p-3"
          v-else
        >
          <component
            v-for="name in renderPageItems"
            :is="renderComponent"
            :key="name"
            :name="name"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import ProxiesCtrl from '@/components/controls/ProxiesCtrl'
import FolderManagerPanel from '@/components/proxies/folders/FolderManagerPanel.vue'
import FolderTopBar from '@/components/proxies/folders/FolderTopBar.vue'
import ProxyGroup from '@/components/proxies/ProxyGroup.vue'
import ProxyGroupForMobile from '@/components/proxies/ProxyGroupForMobile.vue'
import ProxyProvider from '@/components/proxies/ProxyProvider.vue'
import { usePaddingForViews } from '@/composables/paddingViews'
import {
  disableProxiesPageScroll,
  isProxiesPageMounted,
  renderProxiesPageItems,
} from '@/composables/proxies'
import { PROXY_TAB_TYPE } from '@/constant'
import { isMiddleScreen, PROXIES_PAGE } from '@/helper/utils'
import { fetchProxies, proxiesTabShow } from '@/store/proxies'
import { disableProxiesPageTextSelect, twoColumnProxyGroup } from '@/store/settings'
import { folderManagerOpen, isProxyFolderModeActive } from '@/store/proxyFolders'
import { useSessionStorage } from '@vueuse/core'
import { computed, nextTick, onMounted, ref, watch } from 'vue'

const { padding } = usePaddingForViews({
  offsetTop: 0,
  offsetBottom: 0,
})
const renderPageItems = renderProxiesPageItems
const proxiesRef = ref()
const scrollStatus = useSessionStorage('cache/proxies-scroll-status', {
  [PROXY_TAB_TYPE.PROVIDER]: 0,
  [PROXY_TAB_TYPE.PROXIES]: 0,
})

const handleScroll = () => {
  if (!proxiesRef.value) return
  scrollStatus.value[proxiesTabShow.value] = proxiesRef.value.scrollTop
}

const waitTickUntilReady = (startTime = performance.now()) => {
  const proxiesEl = proxiesRef.value
  const isTimedOut = performance.now() - startTime > 300

  if (
    isTimedOut ||
    (proxiesEl && proxiesEl.scrollHeight > scrollStatus.value[proxiesTabShow.value])
  ) {
    if (!proxiesEl) return
    proxiesEl.scrollTo({
      top: scrollStatus.value[proxiesTabShow.value],
      behavior: 'smooth',
    })
  } else {
    requestAnimationFrame(() => {
      waitTickUntilReady(startTime)
    })
  }
}

watch(proxiesTabShow, () =>
  nextTick(() => {
    waitTickUntilReady()
  }),
)

isProxiesPageMounted.value = false

onMounted(() => {
  setTimeout(() => {
    isProxiesPageMounted.value = true
    nextTick(() => {
      waitTickUntilReady()
      fetchProxies()
    })
  })
})

const renderComponent = computed(() => {
  if (proxiesTabShow.value === PROXY_TAB_TYPE.PROVIDER) {
    return ProxyProvider
  }

  if (isMiddleScreen.value && displayTwoColumns.value) {
    return ProxyGroupForMobile
  }

  return ProxyGroup
})

const foldersUiVisible = computed(
  () => isProxyFolderModeActive.value && proxiesTabShow.value === PROXY_TAB_TYPE.PROXIES,
)

const displayTwoColumns = computed(() => {
  if (proxiesTabShow.value === PROXY_TAB_TYPE.PROVIDER && isMiddleScreen.value) {
    return false
  }
  return twoColumnProxyGroup.value && renderPageItems.value.length > 1
})

const filterContent: <T>(all: T[], target: number) => T[] = (all, target) => {
  return all.filter((_, index: number) => index % 2 === target)
}
</script>
