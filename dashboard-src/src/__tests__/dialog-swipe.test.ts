import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'

vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return { blurIntensity: ref(5), swipeInPages: ref(true), swipeInTabs: ref(false) }
})
const navigation = vi.hoisted(() => ({ route: { name: 'proxies' }, push: vi.fn() }))
vi.mock('vue-router', () => ({ useRoute: () => navigation.route, useRouter: () => ({ push: navigation.push }) }))
vi.mock('@/assembly/proxies', async () => ({ proxiesTabShow: (await import('vue')).ref('proxies'), proxyProviederList: (await import('vue')).ref([]) }))
vi.mock('@/assembly/rules', async () => ({ rulesTabShow: (await import('vue')).ref('rules'), ruleProviderList: (await import('vue')).ref([]) }))
vi.mock('@/store/connections', async () => ({ connectionTabShow: (await import('vue')).ref('allConnections') }))
vi.mock('@/helper', async () => ({ renderRoutes: (await import('vue')).ref(['proxies', 'connections', 'overview']) }))
vi.mock('@/helper/utils', async () => ({ isMiddleScreen: (await import('vue')).ref(true) }))
import DialogWrapper from '@/components/common/DialogWrapper.vue'
import { useSwipeRouter } from '@/composables/use-swipe-router'
import { isMiddleScreen } from '@/helper/utils'

let app: App | undefined
let host: HTMLDivElement
let box: HTMLElement
let isOpen: ReturnType<typeof ref<boolean>>
let mobile = true
let reducedMotion = false

const touch = (type: string, y: number, x = 0, target: HTMLElement = box, fingers = 1) => {
  const event = new Event(type, { bubbles: true, cancelable: true })
  const point = { clientX: x, clientY: y }
  Object.defineProperties(event, {
    touches: { value: type === 'touchend' ? [] : Array(fingers).fill(point) },
    changedTouches: { value: [point] },
  })
  target.dispatchEvent(event)
  return event
}
const start = () => touch('touchstart', 100)
const settle = async () => {
  start()
  touch('touchmove', 120)
  touch('touchend', 120)
  await nextTick()
}
const transitionEnd = (propertyName = 'transform') => {
  const event = new Event('transitionend', { bubbles: true })
  Object.defineProperty(event, 'propertyName', { value: propertyName })
  box.dispatchEvent(event)
}

beforeEach(async () => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
  mobile = true
  reducedMotion = false
  vi.spyOn(window, 'matchMedia').mockImplementation((query) => ({
    matches: query.includes('prefers-reduced-motion') ? reducedMotion : mobile,
  }) as MediaQueryList)
  isOpen = ref(true)
  host = document.createElement('div')
  host.id = 'app-content'
  document.body.append(host)
  app = createApp(defineComponent({
    setup: () => () => h(DialogWrapper, {
      modelValue: isOpen.value,
      'onUpdate:modelValue': (value: boolean) => { isOpen.value = value },
      title: 'Test dialog',
    }, () => h('div', { class: 'scroll-content', style: 'overflow-y:auto' }, 'Contents')),
  }))
  app.mount(host)
  await nextTick()
  box = host.querySelector('.modal-box')!
  Object.defineProperty(box, 'offsetHeight', { configurable: true, value: 400 })
})
afterEach(() => {
  app?.unmount()
  host.remove()
  vi.clearAllTimers()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('dialog swipe recovery', () => {
  it('accepts another drag after pulling back to zero without a transition', async () => {
    start()
    touch('touchmove', 140)
    touch('touchmove', 80)
    touch('touchend', 80)
    await nextTick()
    expect(box.style.transform).toBe('')
    start()
    expect(touch('touchmove', 120).defaultPrevented).toBe(true)
    await nextTick()
    expect(box.style.transform).toBe('translate3d(0, 20px, 0)')
    expect(isOpen.value).toBe(true)
  })

  it('recovers after 450ms when transitionend is missing', async () => {
    await settle()
    expect(box.style.transform).toBe('translate3d(0, 0px, 0)')
    await vi.advanceTimersByTimeAsync(449)
    expect(box.style.transform).not.toBe('')
    await vi.advanceTimersByTimeAsync(1)
    expect(box.style.transform).toBe('')
    start()
    touch('touchmove', 120)
    await nextTick()
    expect(box.style.transform).toContain('20px')
  })

  it('only completes on its own transform transition and clears the fallback', async () => {
    await settle()
    expect(vi.getTimerCount()).toBe(1)
    transitionEnd('opacity')
    await nextTick()
    expect(box.style.transform).not.toBe('')
    const childEvent = new Event('transitionend', { bubbles: true })
    Object.defineProperty(childEvent, 'propertyName', { value: 'transform' })
    box.querySelector('.scroll-content')!.dispatchEvent(childEvent)
    await nextTick()
    expect(box.style.transform).not.toBe('')
    transitionEnd()
    await nextTick()
    expect(box.style.transform).toBe('')
    expect(vi.getTimerCount()).toBe(0)
  })

  it('lets a new touch interrupt settling without the old timer cancelling it', async () => {
    await settle()
    start()
    touch('touchmove', 125)
    await nextTick()
    expect(box.style.transform).toBe('translate3d(0, 25px, 0)')
    await vi.advanceTimersByTimeAsync(450)
    expect(box.style.transform).toBe('translate3d(0, 25px, 0)')
  })

  it('clears the fallback on external close and permits a fresh gesture after reopening', async () => {
    await settle()
    expect(vi.getTimerCount()).toBe(1)
    isOpen.value = false
    await nextTick()
    isOpen.value = true
    await nextTick()
    start()
    touch('touchmove', 125)
    await nextTick()
    await vi.advanceTimersByTimeAsync(450)
    expect(box.style.transform).toBe('translate3d(0, 25px, 0)')
    expect(isOpen.value).toBe(true)
  })

  it('clears the pending fallback when the page destroys the dialog', async () => {
    await settle()
    expect(vi.getTimerCount()).toBe(1)
    app!.unmount()
    app = undefined
    expect(vi.getTimerCount()).toBe(0)
  })

  it('resets cancelled zero-offset and reduced-motion gestures immediately', async () => {
    start()
    touch('touchmove', 120)
    touch('touchmove', 80)
    touch('touchcancel', 80)
    await nextTick()
    expect(box.style.transform).toBe('')
    reducedMotion = true
    await settle()
    expect(box.style.transform).toBe('')
    expect(vi.getTimerCount()).toBe(0)
  })

  it('keeps non-top content, horizontal gestures, desktop and multiple touches native', async () => {
    const content = box.querySelector<HTMLElement>('.scroll-content')!
    Object.defineProperties(content, {
      scrollHeight: { configurable: true, value: 500 },
      clientHeight: { configurable: true, value: 100 },
    })
    content.scrollTop = 20
    touch('touchstart', 100, 0, content)
    expect(touch('touchmove', 130, 0, content).defaultPrevented).toBe(false)
    touch('touchend', 130, 0, content)
    start()
    expect(touch('touchmove', 102, 40).defaultPrevented).toBe(false)
    touch('touchend', 102)
    mobile = false
    start()
    expect(touch('touchmove', 130).defaultPrevented).toBe(false)
    touch('touchend', 130)
    mobile = true
    touch('touchstart', 100, 0, box, 2)
    expect(touch('touchmove', 130, 0, box, 2).defaultPrevented).toBe(false)
    touch('touchend', 130)
    await nextTick()
    expect(box.style.transform).toBe('')
    expect(isOpen.value).toBe(true)
  })

  it('still closes a full downward swipe', async () => {
    start()
    touch('touchmove', 200)
    touch('touchend', 200)
    await nextTick()
    expect(isOpen.value).toBe(false)
  })
})

describe('desktop page swipe', () => {
  const mountPage = async () => {
    app?.unmount()
    host.replaceChildren()
    navigation.push.mockReset()
    navigation.push.mockResolvedValue(undefined)
    navigation.route.name = 'proxies'
    isMiddleScreen.value = true
    app = createApp(defineComponent({
      setup() {
        const { swiperRef, pageRef } = useSwipeRouter()
        return () => h('main', { ref: swiperRef }, [h('div', { ref: pageRef }, [
          h('div', { class: 'page-space' }), h('input'),
          h('div', { class: 'horizontal', style: 'overflow-x:auto' }),
        ])])
      },
    }))
    app.mount(host)
    await nextTick()
    const page = host.querySelector<HTMLElement>('main > div')!
    Object.defineProperty(page, 'clientWidth', { configurable: true, value: 640 })
    return page
  }
  const drag = (target: HTMLElement, end = 360) => {
    touch('touchstart', 100, 500, target)
    touch('touchmove', 100, 470, target)
    return touch('touchmove', 100, end, target)
  }
  it('tracks a horizontal drag and restores it on cancellation without navigating', async () => {
    const page = await mountPage()
    expect(drag(page).defaultPrevented).toBe(true)
    expect(page.style.transform).toContain('-110px')
    touch('touchcancel', 100, 360, page)
    await vi.advanceTimersByTimeAsync(400)
    expect(page.style.transform).toBe('')
    expect(navigation.push).not.toHaveBeenCalled()
  })
  it('commits a sufficiently long gesture to the next page', async () => {
    const page = await mountPage()
    drag(page, 100)
    touch('touchend', 100, 100, page)
    await nextTick()
    expect(navigation.push).toHaveBeenCalledWith({ name: 'connections' })
    await vi.advanceTimersByTimeAsync(450)
  })
  it.each(['input', '.horizontal'])('leaves %s input and scrolling alone', async (selector) => {
    const page = await mountPage()
    const target = page.querySelector<HTMLElement>(selector)!
    if (selector === '.horizontal') {
      Object.defineProperty(target, 'scrollWidth', { configurable: true, value: 1000 })
      Object.defineProperty(target, 'clientWidth', { configurable: true, value: 200 })
    }
    expect(drag(target).defaultPrevented).toBe(false)
    expect(page.style.transform).toBe('')
    expect(navigation.push).not.toHaveBeenCalled()
  })
  it('rejects gestures behind an open modal and cleans up a settling animation on unmount', async () => {
    const page = await mountPage()
    const modal = document.createElement('div')
    modal.className = 'modal-open'
    host.append(modal)
    expect(drag(page).defaultPrevented).toBe(false)
    modal.remove()
    drag(page)
    touch('touchcancel', 100, 360, page)
    app!.unmount()
    app = undefined
    expect(page.style.transform).toBe('')
    await vi.advanceTimersByTimeAsync(450)
    expect(navigation.push).not.toHaveBeenCalled()
  })
})