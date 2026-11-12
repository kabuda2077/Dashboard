<template>
  <span class="flex min-w-0 items-center gap-1 overflow-hidden">
    <img
      :src="isSingBoxCore ? SingBoxLogo : MetacubexLogo"
      alt=""
      class="h-4 w-4 rounded-xs"
    />
    <a
      :href="coreRepositoryUrl || undefined"
      :title="updateHint"
      :aria-disabled="!coreRepositoryUrl || undefined"
      :aria-describedby="hintId"
      :tabindex="!coreRepositoryUrl ? 0 : undefined"
      :role="!coreRepositoryUrl ? 'link' : undefined"
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
import { coreRepositoryUrl, isSingBoxCore, version } from '@/assembly/version'
import MetacubexLogo from '@/assets/images/metacubex.jpg'
import SingBoxLogo from '@/assets/images/sing-box.svg'
import { hasHostBridge, postHostMessage } from '@/composables/hostBridge'
import { checkTruncation } from '@/helper/tooltip'
import { computed, useId } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const hintId = useId()
const updateHint = computed(() =>
  t(coreRepositoryUrl.value ? 'desktop.coreRepositoryHint' : 'desktop.unknownCoreSourceHint'),
)

const handleRepositoryClick = (event: MouseEvent) => {
  if (!coreRepositoryUrl.value) {
    event.preventDefault()
    return
  }
  if (!hasHostBridge) return
  event.preventDefault()
  postHostMessage({ type: 'openCoreRepository' })
}
</script>
