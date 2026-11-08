<template>
  <span class="flex min-w-0 items-center gap-1 overflow-hidden">
    <img
      :src="isSingBoxCore ? SingBoxLogo : MetacubexLogo"
      alt=""
      class="h-4 w-4 rounded-xs"
    />
    <a
      :href="coreUpdateSource.releaseUrl || undefined"
      :title="updateHint"
      :aria-disabled="!coreUpdateSource.releaseUrl || undefined"
      :aria-describedby="hintId"
      :tabindex="!coreUpdateSource.releaseUrl ? 0 : undefined"
      :role="!coreUpdateSource.releaseUrl ? 'link' : undefined"
      class="text-base-content/60 hover:text-primary focus-visible:ring-primary/30 min-w-0 truncate rounded-xs transition-colors focus-visible:ring-2 focus-visible:outline-none"
      target="_blank"
      rel="noreferrer"
      @click="handleRepositoryClick"
      @mouseenter="checkTruncation"
    >
      {{ version }}
    </a>
    <span
      :id="hintId"
      class="sr-only"
      >{{ updateHint }}</span
    >
  </span>
</template>

<script setup lang="ts">
import { coreUpdateSource, isSingBoxCore, version } from '@/assembly/version'
import MetacubexLogo from '@/assets/images/metacubex.jpg'
import SingBoxLogo from '@/assets/images/sing-box.svg'
import { hasHostBridge, postHostMessage } from '@/composables/hostBridge'
import { checkTruncation } from '@/helper/tooltip'
import { computed, useId } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const hintId = useId()
const updateHint = computed(() =>
  t(
    coreUpdateSource.value.mode === 'automatic'
      ? 'desktop.coreReleaseHint'
      : coreUpdateSource.value.releaseUrl
        ? 'desktop.manualCoreUpdateHint'
        : 'desktop.unknownCoreSourceHint',
  ),
)

const handleRepositoryClick = (event: MouseEvent) => {
  if (!coreUpdateSource.value.releaseUrl) {
    event.preventDefault()
    return
  }
  if (!hasHostBridge) return
  event.preventDefault()
  postHostMessage({ type: 'openCoreRepository' })
}
</script>
