<template>
  <div
    :class="
      twMerge(
        'latency-tag bg-base-100 h-5 w-10 rounded-xl text-xs select-none md:hover:shadow-sm',
        color,
      )
    "
    @mouseenter="handlerHistoryTip"
  >
    <Transition name="latency-state">
      <span
        v-if="state === 'loading'"
        class="latency-state loading loading-dots loading-xs text-base-content/80"
      ></span>
      <BoltIcon
        v-else-if="state === 'empty'"
        class="latency-state text-base-content h-3 w-3"
      />
      <div
        v-else
        ref="latencyRef"
        class="latency-state tabular-nums"
      >
        {{ latency }}
      </div>
    </Transition>
  </div>
</template>

<script setup lang="ts">
import { NOT_CONNECTED } from '@/constant'
import { getColorForLatency } from '@/helper'
import { useTooltip } from '@/helper/tooltip'
import { getHistoryByName, getLatencyByName } from '@/assembly/proxies'
import { BoltIcon } from '@heroicons/vue/24/outline'
import { CountUp } from 'countup.js'
import dayjs from 'dayjs'
import { twMerge } from 'tailwind-merge'
import { computed, onUnmounted, ref, watch } from 'vue'

const { showTip } = useTooltip()
const handlerHistoryTip = (e: Event) => {
  const history = getHistoryByName(props.name ?? '', props.groupName)

  if (!history.length) return

  const historyList = document.createElement('div')

  historyList.classList.add('flex', 'flex-col', 'gap-1')
  for (const item of history) {
    const itemDiv = document.createElement('div')
    const time = document.createElement('div')
    const latency = document.createElement('div')

    time.textContent = dayjs(item.time).format('YYYY-MM-DD HH:mm:ss')
    latency.textContent = item.delay + 'ms'
    latency.className = getColorForLatency(item.delay)

    itemDiv.classList.add('flex', 'items-center', 'gap-2')
    itemDiv.append(time, latency)
    historyList.append(itemDiv)
  }

  showTip(e, historyList, {
    delay: [1000, 0],
    trigger: 'mouseenter',
    touch: false,
  })
}

const props = defineProps<{
  name?: string
  loading?: boolean
  groupName?: string
}>()
const latencyRef = ref<HTMLElement | null>(null)
const latency = computed(() => getLatencyByName(props.name ?? '', props.groupName))
let countUp: CountUp | null = null
let shownLatency = latency.value

watch(
  latencyRef,
  (element) => {
    if (!element) {
      countUp = null
      return
    }

    countUp = new CountUp(element, latency.value, {
      duration: 1,
      separator: '',
      enableScrollSpy: false,
      startVal: shownLatency,
    })
    countUp.update(latency.value)
    shownLatency = latency.value
  },
  { flush: 'post' },
)

watch(latency, (value) => {
  if (!countUp) return

  countUp.update(value)
  shownLatency = value
})

onUnmounted(() => {
  countUp = null
})

const color = computed(() => {
  return getColorForLatency(latency.value)
})

type LatencyState = 'loading' | 'empty' | 'value'

const state = computed<LatencyState>(() => {
  if (props.loading) return 'loading'
  if (latency.value === NOT_CONNECTED || !latency.value) return 'empty'
  return 'value'
})
</script>

<style scoped>
.latency-tag {
  display: grid;
  place-items: center;
  transition:
    color 0.35s ease-out,
    background-color 0.35s ease-out;
}

.latency-state {
  grid-area: 1 / 1;
}

.latency-state-enter-active,
.latency-state-leave-active {
  transition:
    opacity 0.2s ease-out,
    scale 0.2s cubic-bezier(0.32, 0.72, 0, 1);
}

.latency-state-enter-from,
.latency-state-leave-to {
  opacity: 0;
  scale: 0.6;
}

.latency-state-leave-active {
  pointer-events: none;
}

@media (prefers-reduced-motion: reduce) {
  .latency-tag,
  .latency-state-enter-active,
  .latency-state-leave-active {
    transition: none;
  }
}
</style>
