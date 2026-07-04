<template>
  <div
    v-if="uptime"
    class="text-base-content/60 flex items-center gap-1"
    @mouseenter="showTip($event, tooltip)"
  >
    <ClockIcon class="h-3.5 w-3.5 shrink-0" />
    <span class="truncate">{{ uptime }}</span>
  </div>
</template>

<script setup lang="ts">
import { startedAt } from '@/assembly/version'
import { useTooltip } from '@/helper/tooltip'
import { ClockIcon } from '@heroicons/vue/24/outline'
import dayjs from 'dayjs'
import { computed, onScopeDispose, ref } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const { showTip } = useTooltip()

const now = ref(Date.now())
const timer = setInterval(() => {
  now.value = Date.now()
}, 1000)
onScopeDispose(() => clearInterval(timer))

const tooltip = computed(
  () => `${t('startTime')} - ${dayjs(startedAt.value).format('YYYY-MM-DD HH:mm:ss')}`,
)

const pad = (n: number) => String(n).padStart(2, '0')

const uptime = computed(() => {
  if (!startedAt.value) return ''

  let total = Math.max(0, Math.floor((now.value - startedAt.value) / 1000))
  const days = Math.floor(total / 86400)
  total -= days * 86400
  const hours = Math.floor(total / 3600)
  total -= hours * 3600
  const minutes = Math.floor(total / 60)
  const seconds = total - minutes * 60

  if (days > 0) return `${days}d ${hours}:${pad(minutes)}:${pad(seconds)}`
  if (hours > 0) return `${hours}:${pad(minutes)}:${pad(seconds)}`
  return `${minutes}:${pad(seconds)}`
})
</script>
