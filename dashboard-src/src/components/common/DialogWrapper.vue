<template>
  <Teleport to="#app-content">
    <!--
      :duration 必须显式给：Vue 只测量根元素上的 transition，而移动端抽屉的滑入
      (0.35s) 比遮罩淡出 (0.25s) 长，自动推断会在 0.25s 就掐断离场动画。
    -->
    <Transition
      name="modal"
      :duration="350"
      @after-leave="onAfterLeave"
    >
      <div
        v-show="isOpen"
        ref="backdropRef"
        class="modal"
        :class="{ 'modal-open': isPresenting }"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="title ? 'dialog-title' : undefined"
        @keydown.escape="close"
      >
        <!-- 遮罩层，点击关闭 -->
        <div
          class="modal-backdrop w-screen"
          aria-hidden="true"
          :style="backdropSwipeStyle"
          @click="close"
        />

        <!-- 弹层内容，阻止点击穿透 -->
        <div
          ref="modalBoxRef"
          class="modal-box bg-base-100 relative flex flex-col overflow-hidden p-0 outline-none max-md:max-h-[85dvh] max-md:min-h-[40dvh]"
          :class="[blurIntensity < 5 && 'backdrop-blur-sm!', boxClass]"
          :style="boxSwipeStyle"
          tabindex="-1"
          @click.stop
          @keydown.enter.self="enter"
          @touchstart="onTouchStart"
          @touchmove="onTouchMove"
          @touchend="onTouchEnd"
          @touchcancel="onTouchCancel"
          @transitionend.self="onBoxTransitionEnd"
        >
          <div
            v-if="title && isOpen"
            id="dialog-title"
            class="border-base-content/10 relative shrink-0 border-b px-4 py-2 text-base font-bold"
          >
            {{ title }}
            <slot name="title-right" />
            <button
              type="button"
              class="btn btn-circle btn-ghost btn-xs absolute top-2 right-2"
              aria-label="close"
              @click="close"
            >
              <XMarkIcon class="h-4 w-4" />
            </button>
          </div>
          <!--
            高度区间在移动端由 .modal-box 统一约束（见上面的 max-h-[85dvh] / min-h-[40dvh]），
            这里只负责吃掉剩余空间；桌面端维持原本加在滚动容器上的 90dvh 不变。
            软键盘弹起时 dvh 会跟着 layout viewport 一起收缩（见 index.html 的
            interactive-widget=resizes-content），所以上下限都不会撑破可视高度。
            safe-area 补在滚动容器的 padding 上而不是 .modal-box 上，这样最后一条内容能
            滚到 home indicator 上方，抽屉背景仍然铺满到屏幕物理下缘。
          -->
          <div
            v-if="isOpen"
            class="min-h-0 overflow-y-auto max-md:flex-1 md:max-h-[90dvh]"
            :class="
              noPadding
                ? 'p-0 max-md:pb-[env(safe-area-inset-bottom)]'
                : 'p-4 max-md:pb-[calc(1rem+env(safe-area-inset-bottom))]'
            "
          >
            <slot />
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { blurIntensity } from '@/store/settings'
import { XMarkIcon } from '@heroicons/vue/24/outline'
import { computed, ref, watch, type CSSProperties } from 'vue'

const MOBILE_MEDIA_QUERY = '(width < 48rem)'
const DIRECTION_LOCK_DISTANCE = 10
const VERTICAL_DOMINANCE_RATIO = 1.2
const MIN_FLING_DISTANCE = 48
const CLOSE_VELOCITY = 0.5
const SWIPE_TRANSITION = 'transform 0.35s cubic-bezier(0.32, 0.72, 0, 1)'

type SwipeState = 'idle' | 'pending' | 'dragging' | 'settling' | 'dismissing' | 'rejected'

const isOpen = defineModel<boolean>()
defineProps<{
  noPadding?: boolean
  boxClass?: string
  title?: string
}>()
const emits = defineEmits<{
  (e: 'enter'): void
}>()

const modalBoxRef = ref<HTMLDivElement | undefined>(undefined)
const swipeState = ref<SwipeState>('idle')
const swipeOffset = ref(0)
const swipeAnimating = ref(false)

let startX = 0
let startY = 0
let startTime = 0
let endY = 0

const swipeProgress = computed(() => {
  const height = modalBoxRef.value?.offsetHeight || 1
  return Math.min(swipeOffset.value / height, 1)
})

const boxSwipeStyle = computed<CSSProperties | undefined>(() => {
  if (
    swipeState.value !== 'dragging' &&
    swipeState.value !== 'settling' &&
    swipeState.value !== 'dismissing'
  )
    return

  return {
    transform: `translate3d(0, ${swipeOffset.value}px, 0)`,
    transition: swipeAnimating.value ? SWIPE_TRANSITION : 'none',
    willChange: 'transform',
  }
})

const backdropSwipeStyle = computed<CSSProperties | undefined>(() => {
  if (
    swipeState.value !== 'dragging' &&
    swipeState.value !== 'settling' &&
    swipeState.value !== 'dismissing'
  )
    return

  return {
    opacity: 1 - swipeProgress.value,
    transition: swipeAnimating.value ? 'opacity 0.25s ease-out' : 'none',
  }
})


/*
 * .modal-open 只在弹窗「在场」期间挂着：打开时立刻加上，离场动画跑完才摘掉。
 *
 * 不能直接用静态 class —— 弹窗是 v-show 收起的，元素始终留在 DOM 里，而 daisyUI 的
 * :root:has(.modal-open) 整页滚动锁（base/rootscrolllock.css）不看 display，常挂的
 * .modal-open 会让 <html> 永久 overflow: hidden，移动端浏览器的原生下拉刷新就再也
 * 触发不了（「禁用下拉刷新」只管 body，关掉也没用，见 App.vue）。
 *
 * 也不能直接绑 isOpen：daisyUI 的 .modal 基础态是 visibility: hidden + opacity: 0，
 * 关闭瞬间摘掉 .modal-open，离场动画会被一帧掐断。所以摘除推迟到 after-leave。
 */
const isPresenting = ref(!!isOpen.value)

const onAfterLeave = () => {
  isPresenting.value = false
  resetSwipe()
}

watch(isOpen, (val) => {
  if (val) {
    isPresenting.value = true
    resetSwipe()
    requestAnimationFrame(() => {
      modalBoxRef.value?.focus()
    })
  } else if (swipeState.value !== 'dismissing') {
    // 通过按钮、遮罩或父组件关闭时，清掉可能尚未结束的回弹状态，交还给标准离场动画。
    resetSwipe()
  }
})

function isAtTopOfScrollableAncestors(target: EventTarget | null) {
  const modalBox = modalBoxRef.value
  let element = target instanceof Element ? target : null

  while (element && element !== modalBox) {
    if (element instanceof HTMLElement) {
      const { overflowY } = getComputedStyle(element)
      const isScrollable =
        (overflowY === 'auto' || overflowY === 'scroll') &&
        element.scrollHeight > element.clientHeight

      // 一旦触点所在的任意滚动层还有内容可向上回滚，就把整次手势留给原生滚动。
      // 即使它在本次手势中途滚到顶部，也不接管，避免内容突然变成拖动弹窗。
      if (isScrollable && element.scrollTop > 0) return false
    }
    element = element.parentElement
  }

  return true
}

function onTouchStart(event: TouchEvent) {
  if (swipeState.value === 'dragging') {
    settleSwipe()
    return
  }
  if (swipeState.value !== 'idle') return

  if (
    !isOpen.value ||
    !window.matchMedia(MOBILE_MEDIA_QUERY).matches ||
    event.touches.length !== 1
  ) {
    swipeState.value = 'rejected'
    return
  }

  const touch = event.touches[0]
  startX = touch.clientX
  startY = touch.clientY
  endY = startY
  startTime = performance.now()
  swipeState.value = isAtTopOfScrollableAncestors(event.target) ? 'pending' : 'rejected'
}

function onTouchMove(event: TouchEvent) {
  if (swipeState.value !== 'pending' && swipeState.value !== 'dragging') return
  if (event.touches.length !== 1) {
    onTouchCancel()
    return
  }

  const touch = event.touches[0]
  const deltaX = touch.clientX - startX
  const deltaY = touch.clientY - startY
  endY = touch.clientY

  if (swipeState.value === 'pending') {
    if (Math.max(Math.abs(deltaX), Math.abs(deltaY)) < DIRECTION_LOCK_DISTANCE) return

    if (deltaY > 0 && deltaY >= Math.abs(deltaX) * VERTICAL_DOMINANCE_RATIO) {
      swipeState.value = 'dragging'
    } else if (deltaY < 0 || Math.abs(deltaX) >= Math.abs(deltaY) * VERTICAL_DOMINANCE_RATIO) {
      swipeState.value = 'rejected'
      return
    } else {
      return
    }
  }

  if (swipeState.value !== 'dragging') return

  // 只有方向锁定为「向下关闭」之后才阻止默认行为；正常的纵向滚动不受影响。
  event.preventDefault()
  swipeOffset.value = Math.max(deltaY, 0)
}

function closeDistance() {
  const height = modalBoxRef.value?.offsetHeight || 0
  return Math.min(120, Math.max(72, height * 0.2))
}

function prefersReducedMotion() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

function onTouchEnd(event: TouchEvent) {
  if (swipeState.value === 'settling' || swipeState.value === 'dismissing') return
  if (swipeState.value !== 'dragging') {
    resetSwipe()
    return
  }

  const touch = event.changedTouches[0]
  if (touch) endY = touch.clientY

  const distance = Math.max(endY - startY, 0)
  const duration = Math.max(performance.now() - startTime, 1)
  const isFastFling = distance >= MIN_FLING_DISTANCE && distance / duration >= CLOSE_VELOCITY

  if (distance >= closeDistance() || isFastFling) {
    if (prefersReducedMotion()) {
      resetSwipe()
      close()
      return
    }

    swipeState.value = 'dismissing'
    swipeAnimating.value = true
    swipeOffset.value = modalBoxRef.value?.offsetHeight || window.innerHeight
    close()
    return
  }

  settleSwipe()
}

function onTouchCancel() {
  if (swipeState.value === 'dragging') {
    settleSwipe()
  } else {
    resetSwipe()
  }
}

function settleSwipe() {
  if (prefersReducedMotion()) {
    resetSwipe()
    return
  }

  swipeState.value = 'settling'
  swipeAnimating.value = true
  swipeOffset.value = 0
}

function onBoxTransitionEnd(event: TransitionEvent) {
  if (event.propertyName === 'transform' && swipeState.value === 'settling') resetSwipe()
}

function resetSwipe() {
  swipeState.value = 'idle'
  swipeOffset.value = 0
  swipeAnimating.value = false
  startX = 0
  startY = 0
  startTime = 0
  endY = 0
}

function close() {
  isOpen.value = false
}
function enter() {
  emits('enter')
}
</script>

<style scoped>
/*
 * 动效按项目约定本该集中在 assets/styles/utilities/motion.css，这里是有意保留的例外
 * （motion.css 文件头有对应说明）：过渡类和模板耦合紧，拆开反而更难改。
 * 曲线沿用项目统一的 cubic-bezier(0.32, 0.72, 0, 1)。
 */

/* 遮罩淡入淡出 */
.modal-enter-active,
.modal-leave-active {
  transition: opacity 0.25s ease-out;
}
.modal-enter-from,
.modal-leave-to {
  opacity: 0;
}

/* 桌面端居中卡片：缩放淡入 */
.modal-enter-active .modal-box,
.modal-leave-active .modal-box {
  transition: transform 0.35s cubic-bezier(0.32, 0.72, 0, 1);
}
.modal-enter-from .modal-box,
.modal-leave-to .modal-box {
  transform: scale(0.95);
}

/* 移动端底部抽屉：从屏幕下缘滑入，时长与曲线和路由切换保持一致 */
@media (width < 48rem) {
  .modal-enter-from .modal-box,
  .modal-leave-to .modal-box {
    transform: translateY(100%);
  }
}

/* 降级为纯淡入淡出：去掉位移与缩放，保留 opacity 以免弹窗瞬间闪现/残留 */
@media (prefers-reduced-motion: reduce) {
  .modal-enter-active .modal-box,
  .modal-leave-active .modal-box {
    transition: none;
  }
  .modal-enter-from .modal-box,
  .modal-leave-to .modal-box {
    transform: none;
  }
}
</style>
