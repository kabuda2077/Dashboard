<template>
  <span class="flex min-w-0 items-center gap-1 overflow-hidden">
    <img
      :src="isSingBoxCore ? SingBoxLogo : MetacubexLogo"
      alt=""
      class="h-4 w-4 rounded-xs"
    />
    <a
      :href="repositoryUrl"
      class="text-base-content/60 hover:text-primary focus-visible:ring-primary/30 min-w-0 truncate rounded-xs transition-colors focus-visible:ring-2 focus-visible:outline-none"
      target="_blank"
      rel="noreferrer"
      @click="handleRepositoryClick"
      @mouseenter="checkTruncation"
    >
      {{ version }}
    </a>
  </span>
</template>

<script setup lang="ts">
import { isSingBoxCore, version } from '@/assembly/version'
import MetacubexLogo from '@/assets/images/metacubex.jpg'
import SingBoxLogo from '@/assets/images/sing-box.svg'
import { hasHostBridge, postHostMessage } from '@/composables/hostBridge'
import { checkTruncation } from '@/helper/tooltip'
import { computed } from 'vue'

const repositoryUrl = computed(() =>
  isSingBoxCore.value
    ? 'https://github.com/reF1nd/sing-box'
    : 'https://github.com/MetaCubeX/mihomo',
)

const handleRepositoryClick = (event: MouseEvent) => {
  if (!hasHostBridge) return
  event.preventDefault()
  postHostMessage({ type: 'openCoreRepository' })
}
</script>
