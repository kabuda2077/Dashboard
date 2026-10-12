import { proxiesTabShow, proxyProviederList } from '@/assembly/proxies'
import { ruleProviderList, rulesTabShow } from '@/assembly/rules'
import { CONNECTION_TAB_TYPE, PROXY_TAB_TYPE, ROUTE_NAME, RULE_TAB_TYPE } from '@/constant'
import { renderRoutes } from '@/helper'
import { setPendingSwipeDirection } from '@/helper/page-transition'
import { isMiddleScreen } from '@/helper/utils'
import { connectionTabShow } from '@/store/connections'
import { swipeInPages, swipeInTabs } from '@/store/settings'
import { useEventListener } from '@vueuse/core'
import { flatten } from 'lodash'
import { computed, nextTick, onScopeDispose, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export const disableSwipe = ref(false)

const DIRECTION_LOCK_DISTANCE = 10
const HORIZONTAL_DOMINANCE_RATIO = 1.2
const COMMIT_DISTANCE_RATIO = 0.35
const COMMIT_VELOCITY = 0.4
const VELOCITY_SAMPLE_WINDOW = 100
const EDGE_RESISTANCE = 0.25
const PAGE_DURATION = 350
const MIN_EXIT_DURATION = 120

const SWIPE_CONFLICT_SELECTOR = [
  'input',
  'textarea',
  'select',
  '[contenteditable]:not([contenteditable="false"])',
  'canvas',
  'button',
  'a',
  '[role="button"]',
  '[role="combobox"]',
  '.modal-open',
  '.floating-menu-panel',
  '.drag-handle',
  '.folder-drag-handle',
  '[data-page-swipe-ignore]',
].join(',')

type GestureState = 'idle' | 'pending' | 'dragging' | 'rejected' | 'animating'
type SwipeDirection = 'left' | 'right'
type SwipeEntry = {
  routeName: ROUTE_NAME
  isActive: () => boolean
  activate: () => unknown
}

const prefersReducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches

const hasTextSelection = () => {
  const selection = window.getSelection()
  return Boolean(selection && !selection.isCollapsed && selection.toString().length)
}

const isTextEditingElement = (element: Element | null) => {
  if (!element) return false
  if (element instanceof HTMLTextAreaElement) return true
  if (element instanceof HTMLElement && element.isContentEditable) return true
  if (!(element instanceof HTMLInputElement)) return false

  return ![
    'button',
    'checkbox',
    'color',
    'file',
    'hidden',
    'image',
    'radio',
    'range',
    'reset',
    'submit',
  ].includes(element.type)
}

const isHorizontallyScrollable = (element: Element) => {
  const { overflowX } = getComputedStyle(element)
  return (
    (overflowX === 'auto' || overflowX === 'scroll') && element.scrollWidth > element.clientWidth
  )
}

const hasSwipeConflict = (event: TouchEvent, root: HTMLElement) => {
  for (const target of event.composedPath()) {
    if (!(target instanceof Element)) continue
    if (target.matches(SWIPE_CONFLICT_SELECTOR) || isHorizontallyScrollable(target)) return true
    if (target === root) break
  }

  return false
}

export const useSwipeRouter = () => {
  const swiperRef = ref<HTMLElement | null>(null)
  const pageRef = ref<HTMLElement | null>(null)
  const route = useRoute()
  const router = useRouter()
  let disposed = false
  const pendingAnimations = new Set<() => void>()
  const pendingTimers = new Set<number>()
  onScopeDispose(() => {
    disposed = true
    for (const finish of pendingAnimations) finish()
    for (const timer of pendingTimers) clearTimeout(timer)
    setPendingSwipeDirection('')
    setOffset(0)
  })

  const swipeList = computed<SwipeEntry[]>(() => {
    return flatten(
      renderRoutes.value.map((r): SwipeEntry[] => {
        if (swipeInTabs.value) {
          if (r === ROUTE_NAME.proxies && proxyProviederList.value.length > 0) {
            return Object.values(PROXY_TAB_TYPE).map((tab) => ({
              routeName: r,
              isActive: () => route.name === r && proxiesTabShow.value === tab,
              activate: () => {
                proxiesTabShow.value = tab
                return router.push({ name: r })
              },
            }))
          } else if (r === ROUTE_NAME.connections) {
            return Object.values(CONNECTION_TAB_TYPE).map((tab) => ({
              routeName: r,
              isActive: () => route.name === r && connectionTabShow.value === tab,
              activate: () => {
                connectionTabShow.value = tab
                return router.push({ name: r })
              },
            }))
          } else if (r === ROUTE_NAME.rules && ruleProviderList.value.length > 0) {
            return Object.values(RULE_TAB_TYPE).map((tab) => ({
              routeName: r,
              isActive: () => route.name === r && rulesTabShow.value === tab,
              activate: () => {
                rulesTabShow.value = tab
                return router.push({ name: r })
              },
            }))
          }
        }

        return [
          {
            routeName: r,
            isActive: () => route.name === r,
            activate: () => router.push({ name: r }),
          },
        ]
      }),
    )
  })

  const resolveTarget = (direction: SwipeDirection) => {
    const list = swipeList.value
    const index = list.findIndex((entry) => entry.isActive())
    if (index === -1 || list.length < 2) return undefined

    const step = direction === 'left' ? 1 : -1
    return list[(index + step + list.length) % list.length]
  }

  const canCommit = (direction: SwipeDirection) => {
    return Boolean(resolveTarget(direction))
  }

  const canSwipeNow = () => {
    return (
      isMiddleScreen.value &&
      swipeInPages.value &&
      !disposed &&
      !disableSwipe.value &&
      !document.querySelector('dialog:modal, .modal-open, .floating-menu-panel') &&
      !isTextEditingElement(document.activeElement) &&
      !hasTextSelection()
    )
  }

  const setOffset = (offset: number, transition = 'none') => {
    const el = pageRef.value
    if (!el) return
    el.style.transition = transition
    el.style.transform = offset ? `translate3d(${offset}px, 0, 0)` : ''
  }

  const animateOffset = (offset: number, duration: number) => {
    return new Promise<void>((resolve) => {
      const el = pageRef.value
      if (!el || duration <= 0 || prefersReducedMotion()) {
        setOffset(offset)
        resolve()
        return
      }

      let timer = 0
      const finish = () => {
        el.removeEventListener('transitionend', onEnd)
        clearTimeout(timer)
        pendingAnimations.delete(finish)
        resolve()
      }
      const onEnd = (event: TransitionEvent) => {
        if (event.target === el && event.propertyName === 'transform') finish()
      }

      pendingAnimations.add(finish)
      el.addEventListener('transitionend', onEnd)
      timer = window.setTimeout(finish, duration + 50)
      void el.offsetWidth
      setOffset(offset, `transform ${duration}ms var(--page-transition-ease)`)
    })
  }

  let gestureState: GestureState = 'idle'
  let startX = 0
  let startY = 0
  let originX = 0
  let offset = 0
  let samples: { x: number; t: number }[] = []
  let awaitingLeave = false

  const pushSample = (x: number) => {
    const now = performance.now()
    samples.push({ x, t: now })
    samples = samples.filter((sample) => now - sample.t <= VELOCITY_SAMPLE_WINDOW)
  }

  const getVelocity = () => {
    if (samples.length < 2) return 0
    const first = samples[0]
    const last = samples[samples.length - 1]
    const duration = last.t - first.t
    return duration > 0 ? (last.x - first.x) / duration : 0
  }

  const resolveOffset = (deltaX: number) => {
    const direction: SwipeDirection = deltaX < 0 ? 'left' : 'right'
    return canCommit(direction) ? deltaX : deltaX * EDGE_RESISTANCE
  }

  const settle = async () => {
    gestureState = 'animating'
    await animateOffset(0, PAGE_DURATION)
    gestureState = 'idle'
  }

  const exitDuration = (width: number) => {
    return Math.max(MIN_EXIT_DURATION, (PAGE_DURATION * (width - Math.abs(offset))) / width)
  }

  const commit = async (direction: SwipeDirection) => {
    gestureState = 'animating'
    const el = pageRef.value!
    const width = el.clientWidth
    const exitOffset = direction === 'left' ? -width : width

    const target = resolveTarget(direction)
    if (!target) return settle()

    if (target.routeName === route.name) {
      await animateOffset(exitOffset, exitDuration(width))
      if (disposed) return
      await target.activate()
      await nextTick()
      setOffset(-exitOffset)
      await animateOffset(0, PAGE_DURATION)
      gestureState = 'idle'
      return
    }

    el.style.setProperty('--swipe-offset', `${offset}px`)
    setPendingSwipeDirection(direction === 'left' ? 'slide-left' : 'slide-right')
    awaitingLeave = true
    await target.activate()
    await nextTick()

    if (awaitingLeave) {
      awaitingLeave = false
      el.style.removeProperty('--swipe-offset')
      return settle()
    }

    const timer = window.setTimeout(() => {
      pendingTimers.delete(timer)
      el.style.removeProperty('--swipe-offset')
      gestureState = 'idle'
    }, PAGE_DURATION)
    pendingTimers.add(timer)
  }

  const onPageBeforeLeave = () => {
    if (!awaitingLeave) return
    awaitingLeave = false
    setOffset(0)
  }

  const onTouchStart = (event: TouchEvent) => {
    if (gestureState === 'animating') return
    if (gestureState === 'dragging') {
      gestureState = 'rejected'
      settle()
      return
    }
    if (event.touches.length !== 1) {
      gestureState = 'rejected'
      return
    }

    const touch = event.touches[0]
    if (!touch) return
    startX = touch.clientX
    startY = touch.clientY
    offset = 0
    samples = []

    const root = swiperRef.value
    gestureState = root && canSwipeNow() && !hasSwipeConflict(event, root) ? 'pending' : 'rejected'
  }

  const onTouchMove = (event: TouchEvent) => {
    if (gestureState !== 'pending' && gestureState !== 'dragging') return
    if (event.touches.length !== 1) {
      if (gestureState === 'dragging') settle()
      else gestureState = 'rejected'
      return
    }

    const touch = event.touches[0]
    if (!touch || !canSwipeNow()) {
      if (gestureState === 'dragging') void settle()
      else gestureState = 'rejected'
      return
    }

    if (gestureState === 'pending') {
      const distanceX = Math.abs(touch.clientX - startX)
      const distanceY = Math.abs(touch.clientY - startY)
      if (Math.max(distanceX, distanceY) < DIRECTION_LOCK_DISTANCE) return

      if (distanceX < distanceY * HORIZONTAL_DOMINANCE_RATIO) {
        gestureState = 'rejected'
        return
      }

      gestureState = 'dragging'
      originX = touch.clientX
    }

    if (event.cancelable) event.preventDefault()
    pushSample(touch.clientX)
    offset = resolveOffset(touch.clientX - originX)
    setOffset(offset)
  }

  const onTouchEnd = (event: TouchEvent) => {
    if (gestureState !== 'dragging') {
      if (gestureState !== 'animating') gestureState = 'idle'
      return
    }

    const touch = event.changedTouches[0]
    if (touch) pushSample(touch.clientX)

    const el = pageRef.value
    const velocity = getVelocity()
    const direction: SwipeDirection = offset < 0 ? 'left' : 'right'
    const sameDirection = Math.sign(velocity) === Math.sign(offset)
    const isFling = Math.abs(velocity) >= COMMIT_VELOCITY
    const passedDistance = el ? Math.abs(offset) >= el.clientWidth * COMMIT_DISTANCE_RATIO : false
    const shouldCommit =
      offset !== 0 &&
      canCommit(direction) &&
      canSwipeNow() &&
      (isFling ? sameDirection : passedDistance)

    if (shouldCommit) commit(direction)
    else settle()
  }

  const onTouchCancel = () => {
    if (gestureState === 'dragging') settle()
    else if (gestureState !== 'animating') gestureState = 'idle'
  }

  useEventListener(swiperRef, 'touchstart', onTouchStart, { passive: true })
  useEventListener(swiperRef, 'touchmove', onTouchMove, { passive: false })
  useEventListener(swiperRef, 'touchend', onTouchEnd, { passive: true })
  useEventListener(swiperRef, 'touchcancel', onTouchCancel, { passive: true })

  return {
    swiperRef,
    pageRef,
    onPageBeforeLeave,
  }
}
