<template>
  <div
    class="group relative h-22 cursor-pointer"
    :data-group-name="proxyGroup.name"
    ref="cardWrapperRef"
    tabindex="0"
    :data-page-swipe-ignore="modalMode || undefined"
    :aria-expanded="modalMode"
    :aria-label="proxyGroup.name"
    @click="handlerGroupClick"
    @keydown.enter.self.prevent="handlerGroupClick"
    @keydown.space.self.prevent="handlerGroupClick"
  >
    <Transition name="proxy-group-backdrop">
      <div v-if="modalMode" class="fixed inset-0 z-40 overflow-hidden bg-black/40" aria-hidden="true" />
    </Transition>
    <div
      class="base-container proxy-group-card absolute flex flex-col gap-2 overflow-hidden p-2 will-change-transform"
      :class="modalMode && 'overlay-glass'"
      :role="modalMode ? 'dialog' : undefined"
      :aria-modal="modalMode || undefined"
      :aria-label="proxyGroup.name"
      tabindex="-1"
      :style="cardStyle"
      @contextmenu.prevent.stop="handlerLatencyTest"
      @transitionend.self="handlerTransitionEnd"
      @keydown="handleCardKeydown"
      ref="cardRef"
    >
      <ProxyGroupHeaderForMobile
        :name="name"
        :proxies-count="proxiesCount"
        :is-latency-testing="isLatencyTesting"
        :display-content="displayContent"
        @latency-test="handlerLatencyTest"
      />

      <div
        v-if="displayContent"
        class="proxy-group-card-content will-change-opacity max-h-108 overflow-y-auto overscroll-contain"
        :class="[PROXIES_PARENT_CLASS]"
        :style="{
          width: 'calc(100vw - 2.5rem)',
          opacity: contentOpacity,
          contain: 'layout style paint',
        }"
      >
        <Component
          :is="groupProxiesByProvider ? ProxiesByProvider : ProxiesContent"
          :name="name"
          :now="proxyGroup.now"
          :render-proxies="renderProxies"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useBounceOnVisible } from '@/composables/use-bounce-on-visible'
import { disableProxiesPageScroll } from '@/helper/proxies'
import { useRenderProxyList } from '@/composables/use-render-proxy-list'
import { PROXIES_PARENT_CLASS } from '@/helper/utils'
import { proxyGroupLatencyTest } from '@/assembly/proxies'
import { proxyMap } from '@/assembly/proxies'
import { groupProxiesByProvider } from '@/store/settings'
import { useEventListener } from '@vueuse/core'
import { computed, nextTick, onUnmounted, ref } from 'vue'
import ProxiesByProvider from './ProxiesByProvider.vue'
import ProxiesContent from './ProxiesContent.vue'
import ProxyGroupHeaderForMobile from './ProxyGroupHeaderForMobile.vue'

const props = defineProps<{
  name: string
}>()
const proxyGroup = computed(() => proxyMap.value[props.name])
const allProxies = computed(() => proxyGroup.value.all ?? [])
const { proxiesCount, renderProxies } = useRenderProxyList(allProxies, props.name)
const isLatencyTesting = ref(false)

const modalMode = ref(false)
const displayContent = ref(false)
const contentOpacity = ref(0)

const cardWrapperRef = ref<HTMLElement>()
const cardRef = ref<HTMLElement>()
let frame = 0
let transitionTimer = 0
let returnFocus: HTMLElement | null = null

const INIT_STYLE = {
  width: '100%',
  maxHeight: '100%',
  top: 0,
  left: 0,
  right: 0,
  bottom: 0,
  zIndex: 1,
  transform: 'translate3d(0, 0, 0) scale(1)',
}
const cardStyle = ref<Record<string, string | number>>({
  ...INIT_STYLE,
})

const calcCardStyle = () => {
  cancelAnimationFrame(frame)
  frame = requestAnimationFrame(() => {
    if (!cardWrapperRef.value) return

    if (!modalMode.value) {
      cardStyle.value = {
        ...cardStyle.value,
        width: '100%',
        maxHeight: '100%',
        transform: 'translate3d(0, 0, 0) scale(1)',
        zIndex: 50,
      }
      return
    }

    const manyProxies = renderProxies.value.length > 4
    const { left, top, bottom } = cardWrapperRef.value.getBoundingClientRect()
    const { innerHeight, innerWidth } = window

    const minSafeArea = innerHeight * 0.15
    const baseLine = innerHeight * 0.2
    const maxSafeArea = innerHeight * 0.3

    const isLeft = left < innerWidth / 3
    const isTop = (top + bottom) * 0.5 < innerHeight * (manyProxies ? 0.7 : 0.5)
    const transformOrigin = isLeft
      ? isTop
        ? 'top left'
        : 'bottom left'
      : isTop
        ? 'top right'
        : 'bottom right'
    const positionKeyX = isLeft ? 'left' : 'right'
    const positionKeyY = isTop ? 'top' : 'bottom'

    let transformValueY = 0
    let verticalOffset = 0

    if (isTop) {
      if (top < minSafeArea || (top > maxSafeArea && manyProxies)) {
        transformValueY = baseLine - top
      }
      verticalOffset = top + transformValueY
    } else {
      const minSafeBottom = innerHeight - minSafeArea
      const maxSafeBottom = innerHeight - maxSafeArea
      const baseLineBottom = innerHeight - baseLine

      if (bottom > minSafeBottom || (bottom < maxSafeBottom && manyProxies)) {
        transformValueY = baseLineBottom - bottom
      }
      verticalOffset = innerHeight - bottom - transformValueY
    }

    cardStyle.value = {
      width: 'calc(100vw - 1.5rem)',
      maxHeight: `${Math.max(120, innerHeight - verticalOffset - 112)}px`,
      transform: `translate3d(0, ${transformValueY}px, 0) scale(1)`,
      transformOrigin,
      zIndex: 50,
      [positionKeyY]: 0,
      [positionKeyX]: 0,
    }
  })
}

const finishTransition = () => {
  clearTimeout(transitionTimer)
  if (modalMode.value) contentOpacity.value = 1
  else {
    displayContent.value = false
    cardStyle.value = { ...INIT_STYLE }
  }
}
const handlerTransitionEnd = (e: TransitionEvent) => {
  if (e.propertyName === 'width') finishTransition()
}
const handlerGroupClick = async () => {
  clearTimeout(transitionTimer)
  modalMode.value = !modalMode.value
  disableProxiesPageScroll.value = modalMode.value
  if (modalMode.value) {
    returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    displayContent.value = true
    await nextTick()
    cardRef.value?.focus({ preventScroll: true })
  } else {
    const target = returnFocus?.isConnected && returnFocus !== document.body ? returnFocus : cardWrapperRef.value
    target?.focus({ preventScroll: true })
  }
  contentOpacity.value = 0
  calcCardStyle()
  transitionTimer = window.setTimeout(finishTransition, 260)
}
const handleCardKeydown = (event: KeyboardEvent) => {
  if (!modalMode.value) return
  if (event.key === 'Escape') {
    event.preventDefault()
    event.stopPropagation()
    void handlerGroupClick()
  } else if (event.key === 'Tab') {
    const targets = [...(cardRef.value?.querySelectorAll<HTMLElement>('button, input, select, [tabindex="0"]') ?? [])]
      .filter((element) => !element.hasAttribute('disabled'))
    const first = targets[0], last = targets.at(-1)
    if (!first || (event.shiftKey && (document.activeElement === first || document.activeElement === cardRef.value))) {
      event.preventDefault()
      ;(last ?? cardRef.value)?.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }
}
useEventListener(window, 'resize', () => { if (modalMode.value) calcCardStyle() })

const handlerLatencyTest = async () => {
  if (isLatencyTesting.value) return

  isLatencyTesting.value = true
  try {
    await proxyGroupLatencyTest(props.name)
    isLatencyTesting.value = false
  } catch {
    isLatencyTesting.value = false
  }
}

onUnmounted(() => {
  cancelAnimationFrame(frame)
  clearTimeout(transitionTimer)
  if (modalMode.value) {
    disableProxiesPageScroll.value = false
  }
})

useBounceOnVisible(cardRef)
</script>

<style scoped>
.proxy-group-backdrop-enter-active,
.proxy-group-backdrop-leave-active {
  transition: opacity 0.25s ease-out;
}
.proxy-group-backdrop-enter-from,
.proxy-group-backdrop-leave-to { opacity: 0; }
.proxy-group-card {
  --proxy-group-card-motion: 0.2s cubic-bezier(0, 0, 0.2, 1);
  transition: width var(--proxy-group-card-motion), transform var(--proxy-group-card-motion), max-height var(--proxy-group-card-motion);
}
.proxy-group-card-content { transition: opacity var(--proxy-group-card-motion); }
@media (prefers-reduced-motion: reduce) {
  .proxy-group-card, .proxy-group-card-content,
  .proxy-group-backdrop-enter-active, .proxy-group-backdrop-leave-active { transition: none; }
}
</style>
