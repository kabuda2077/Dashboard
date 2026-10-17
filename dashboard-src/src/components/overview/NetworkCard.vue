<template>
  <div class="network-card base-container w-full p-4">
    <div class="network-card-grid grid grid-cols-1 gap-3">
      <div class="network-card-tests bg-base-200/30 relative min-w-0 overflow-hidden rounded-xl">
        <div
          ref="deck"
          class="network-test-deck"
          tabindex="0"
          role="region"
          :aria-label="$t('networkTestsNavigation')"
          data-page-swipe-ignore
          @keydown="onKeydown"
          @wheel="onWheel"
          @scroll="updateScroll"
        >
          <ConnectionStatus />
          <RoutingCheck />
        </div>
        <div
          class="network-scroll-track"
          aria-hidden="true"
          @pointerdown="onTrackPointer"
        >
          <span
            class="network-scroll-thumb"
            :style="{ transform: `translateY(${scrollProgress * 50}%)` }"
          />
        </div>
      </div>
      <IPCheck />
    </div>
  </div>
</template>

<script setup lang="ts">
import ConnectionStatus from '@/components/overview/ConnectionStatus.vue'
import IPCheck from '@/components/overview/IPCheck.vue'
import RoutingCheck from '@/components/overview/RoutingCheck.vue'
import { onUnmounted, ref } from 'vue'

const deck = ref<HTMLElement>()
const scrollProgress = ref(0)
const updateScroll = () => {
  if (!deck.value) return
  const range = deck.value.scrollHeight - deck.value.clientHeight
  scrollProgress.value = range > 0 ? Math.max(0, Math.min(1, deck.value.scrollTop / range)) : 0
}
let animation: number | undefined
const scrollToCard = (top: number) => {
  const element = deck.value
  if (!element) return
  if (animation !== undefined) cancelAnimationFrame(animation)
  const from = element.scrollTop
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  element.style.scrollSnapType = 'none'
  const started = performance.now()
  const step = (now: number) => {
    const progress = reducedMotion ? 1 : Math.min(1, (now - started) / 260)
    element.scrollTop = from + (top - from) * (1 - (1 - progress) ** 3)
    updateScroll()
    if (progress < 1) animation = requestAnimationFrame(step)
    else {
      animation = undefined
      element.style.scrollSnapType = ''
    }
  }
  animation = requestAnimationFrame(step)
}
onUnmounted(() => {
  if (animation !== undefined) cancelAnimationFrame(animation)
})
const onTrackPointer = (event: PointerEvent) => {
  if (!deck.value || event.button !== 0) return
  const track = event.currentTarget as HTMLElement
  const rect = track.getBoundingClientRect()
  scrollToCard(event.clientY < rect.top + rect.height / 2 ? 0 : deck.value.clientHeight)
}
let wheelUntil = 0
let wheelDelta = 0
const onWheel = (event: WheelEvent) => {
  if (
    !deck.value ||
    event.ctrlKey ||
    Math.abs(event.deltaX) > Math.abs(event.deltaY) ||
    !event.deltaY
  )
    return
  if (performance.now() < wheelUntil) {
    event.preventDefault()
    return
  }
  const height = deck.value.clientHeight
  const direction = Math.sign(event.deltaY)
  const index = Math.round(deck.value.scrollTop / height)
  const next = index + direction
  if (next < 0 || next > 1) {
    wheelDelta = 0
    return // At the outer edges, let the page scroll normally.
  }
  event.preventDefault()
  if (Math.sign(wheelDelta) !== direction) wheelDelta = 0
  wheelDelta += event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? height : 1)
  if (Math.abs(wheelDelta) < 24) return
  wheelDelta = 0
  wheelUntil = performance.now() + 350
  scrollToCard(next * height)
}
const onKeydown = (event: KeyboardEvent) => {
  if (event.target !== deck.value || !deck.value) return
  const direction = ['ArrowDown', 'PageDown'].includes(event.key)
    ? 1
    : ['ArrowUp', 'PageUp'].includes(event.key)
      ? -1
      : 0
  if (!direction) return
  const next = Math.max(
    0,
    Math.min(deck.value.clientHeight, deck.value.scrollTop + direction * deck.value.clientHeight),
  )
  if (Math.abs(next - deck.value.scrollTop) < 1) return
  event.preventDefault()
  scrollToCard(next)
}
</script>

<style>
.network-card {
  container-type: inline-size;
}
.network-card-tests {
  container-type: inline-size;
  container-name: network-tests;
  min-height: 18rem;
}
.network-test-deck {
  /* Let the IP content size the shared row without expanding both scroll pages. */
  position: absolute;
  inset: 0;
  height: 100%;
  overflow-y: auto;
  overflow-x: hidden;
  scroll-snap-type: y mandatory;
  scrollbar-width: none;
}
.network-test-deck:focus-visible {
  outline: 1px solid var(--color-primary);
  outline-offset: -1px;
}
.network-test-deck::-webkit-scrollbar {
  display: none;
}
.network-scroll-track {
  position: absolute;
  top: 50%;
  /* Align the 22px track hit area with the 24px header action's center. */
  right: calc(1rem + 1px);
  height: 5.5rem;
  width: 22px;
  transform: translateY(-50%);
  opacity: 0;
  transition: opacity 150ms ease;
  cursor: pointer;
}
.network-card-tests:hover > .network-scroll-track {
  opacity: 1;
}
.network-scroll-track::before {
  content: '';
  position: absolute;
  inset-block: 0;
  left: 50%;
  width: 5px;
  transform: translateX(-50%);
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-base-content) 7%, transparent);
}
.network-scroll-thumb {
  position: relative;
  top: 2px;
  display: block;
  width: 5px;
  height: calc((100% - 4px) * 2 / 3);
  margin-inline: auto;
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-base-content) 31%, transparent);
}
@media (prefers-reduced-motion: reduce) {
  .network-scroll-track {
    transition: none;
  }
}
.network-test-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  padding: 1rem;
  scroll-snap-align: start;
  scroll-snap-stop: always;
}
.network-test-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  grid-template-rows: repeat(4, minmax(0, 1fr));
  flex: 1;
  gap: 0.5rem 1rem;
  margin-top: 0.5rem;
  margin-left: 0.25rem;
  /* Keep results clear of the scrollbar while the header action stays aligned. */
  margin-right: calc(22px + 0.5rem);
  min-height: 0;
}
.network-latency-grid {
  column-gap: 1.5rem;
}
@container network-tests (min-width: 520px) {
  .network-test-grid {
    grid-template-columns: repeat(4, minmax(0, 1fr));
    grid-template-rows: repeat(2, minmax(0, 1fr));
    column-gap: 0.5rem;
  }
  .network-latency-grid {
    column-gap: 1.25rem;
  }
}
@container (min-width: 768px) {
  .network-card-grid {
    grid-template-columns: minmax(0, 11fr) minmax(0, 9fr);
  }
}
@container (min-width: 800px) {
  .network-card-tests {
    min-height: 16.5rem;
  }
}
</style>
