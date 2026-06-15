<template>
  <div
    ref="topBarRef"
    class="bg-base-100/80 scrollbar-hidden border-base-300/50 sticky z-10 flex items-center gap-1 overflow-x-auto shadow backdrop-blur-xl transition-all duration-300"
    :class="isStuck ? 'm-0 rounded-none px-4 pt-2 pb-1.5' : 'mx-3 mt-3 rounded-xl p-1'"
    style="top: -1px"
    @touchstart="disableSwipe = true"
    @touchend="disableSwipe = false"
    @touchcancel="disableSwipe = false"
  >
    <FolderItem
      :id="VIRTUAL_ALL"
      :label="$t('folder_all')"
      :count="totalCount"
      :is-active="activeFolderId === VIRTUAL_ALL"
      orientation="horizontal"
      icon="all"
      @activate="activeFolderId = VIRTUAL_ALL"
    />
    <FolderItem
      v-for="f in foldersSorted"
      :key="f.id"
      :id="f.id"
      :label="displayFolderName(f.name)"
      :count="folderCount(f.id)"
      :is-active="activeFolderId === f.id"
      orientation="horizontal"
      icon="folder"
      @activate="activeFolderId = f.id"
    />
    <FolderItem
      v-if="folderCount(VIRTUAL_UNCAT) > 0"
      :id="VIRTUAL_UNCAT"
      :label="$t('folder_uncategorized')"
      :count="folderCount(VIRTUAL_UNCAT)"
      :is-active="activeFolderId === VIRTUAL_UNCAT"
      orientation="horizontal"
      icon="uncategorized"
      @activate="activeFolderId = VIRTUAL_UNCAT"
    />
    <button
      class="btn btn-ghost btn-sm ml-auto shrink-0"
      @click="folderManagerOpen = !folderManagerOpen"
      :title="$t('folder_manage')"
    >
      <Cog6ToothIcon class="h-4 w-4" />
    </button>
  </div>
</template>

<script setup lang="ts">
import { disableSwipe } from '@/composables/swipe'
import { proxyGroupList } from '@/store/proxies'
import {
  activeFolderId,
  folderCount,
  folderManagerOpen,
  folders,
  VIRTUAL_ALL,
  VIRTUAL_UNCAT,
} from '@/store/proxyFolders'
import { Cog6ToothIcon } from '@heroicons/vue/24/outline'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import FolderItem from './FolderItem.vue'
import { displayFolderName } from './folderName'

const foldersSorted = computed(() => [...folders.value].sort((a, b) => a.order - b.order))
const totalCount = computed(() => proxyGroupList.value.length)

const topBarRef = ref<HTMLElement | null>(null)
const isStuck = ref(false)
let observer: IntersectionObserver | null = null

onMounted(() => {
  if (!topBarRef.value) return
  observer = new IntersectionObserver(([entry]) => (isStuck.value = entry.intersectionRatio < 1), {
    threshold: [1],
  })
  observer.observe(topBarRef.value)
})

onBeforeUnmount(() => observer?.disconnect())
</script>
