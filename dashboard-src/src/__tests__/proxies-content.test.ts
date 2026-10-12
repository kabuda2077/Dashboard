import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App, type ComputedRef, type Ref } from 'vue'
import type { Virtualizer, VirtualizerOptions } from '@tanstack/vue-virtual'

const calls = vi.hoisted(() => ({ scroll: vi.fn(), select: vi.fn(), request: vi.fn() }))
const geometry = vi.hoisted(() => ({ cardHeight: 48, viewportHeight: 104 }))
let virtualizer: Ref<Virtualizer<HTMLElement, Element>>
let gridWidth: Ref<number>
vi.mock('@/assembly/proxies', () => ({ handlerProxySelect: calls.select }))
vi.mock('@/helper/request-error', () => ({ runManualRequest: calls.request }))
vi.mock('@/helper/utils', () => ({ PROXIES_PARENT_CLASS: 'proxies-scrollable-parent' }))
vi.mock('@/composables/use-collapse-transition', () => ({ useCollapseTransition: () => undefined }))
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return { minProxyCardWidth: ref(150), proxyCardSize: ref('small') }
})
vi.mock('@vueuse/core', async () => {
  const { ref } = await import('vue')
  return { useElementSize: () => {
    gridWidth = ref(200)
    return { width: gridWidth }
  }, useResizeObserver: () => {} }
})
vi.mock('@/components/proxies/ProxyNodeCard.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    props: ['name', 'active'],
    setup: (props) => () => h('button', { 'data-node': props.name, 'data-active': props.active }, props.name),
  }) }
})
// Keep the real TanStack virtualizer and Vue adapter; provide deterministic layout observations
// because Happy DOM does not implement browser layout or scroll measurements.
vi.mock('@tanstack/vue-virtual', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/vue-virtual')>()
  const { computed } = await import('vue')
  return { ...actual, useVirtualizer: (options: ComputedRef<VirtualizerOptions<HTMLElement, Element>>) => {
    virtualizer = actual.useVirtualizer(computed(() => ({
      ...options.value,
      observeElementRect: (_: unknown, callback: (rect: { width: number; height: number }) => void) => {
        callback({ width: 200, height: geometry.viewportHeight })
        return () => {}
      },
      observeElementOffset: (_: unknown, callback: (offset: number, scrolling: boolean) => void) => {
        callback(0, false)
        return () => {}
      },
      scrollToFn: calls.scroll,
    })))
    return virtualizer
  } }
})

import ProxiesContent from '@/components/proxies/ProxiesContent.vue'
import { PROXY_CARD_SIZE } from '@/constant'
import { proxyCardSize } from '@/store/settings'

let app: App | undefined
let scroller: HTMLDivElement
let nodes: Ref<string[]>
let now: Ref<string | undefined>
const rowHeight = (element: HTMLElement) => geometry.cardHeight + (element.classList.contains('pb-2') ? 8 : 0)
const rows = () => [...scroller.querySelectorAll<HTMLElement>('[data-index]')]
const mount = async (names: string[], active?: string) => {
  nodes = ref(names)
  now = ref(active)
  app = createApp(defineComponent({
    setup: () => () => h(ProxiesContent, { name: 'group', now: now.value, renderProxies: nodes.value }),
  }))
  app.mount(scroller)
  await nextTick()
  await nextTick()
}
const measureRows = async () => {
  // A browser ResizeObserver supplies these same changed layout dimensions.
  for (const row of rows()) virtualizer.value.measureElement(row)
  await nextTick()
}

beforeEach(() => {
  vi.clearAllMocks()
  geometry.cardHeight = 48
  geometry.viewportHeight = 104
  proxyCardSize.value = PROXY_CARD_SIZE.SMALL
  calls.request.mockImplementation((operation: () => unknown) => operation())
  scroller = document.createElement('div')
  scroller.className = 'proxies-scrollable-parent'
  document.body.append(scroller)
  Object.defineProperties(scroller, {
    clientHeight: { configurable: true, get: () => geometry.viewportHeight },
    scrollHeight: { configurable: true, get: () => virtualizer?.value.getTotalSize() ?? 0 },
  })
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (this: HTMLElement) {
    return this.hasAttribute('data-index') ? rowHeight(this) : 0
  })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const index = Number(this.dataset.index ?? 0)
    const top = index * (geometry.cardHeight + 8) - scroller.scrollTop
    const height = this.hasAttribute('data-index') ? rowHeight(this) : geometry.viewportHeight
    return { top, bottom: top + height, left: 0, right: 200, width: 200, height, x: 0, y: top, toJSON: () => ({}) }
  })
})
afterEach(() => {
  app?.unmount()
  app = undefined
  scroller.remove()
  vi.restoreAllMocks()
})

describe('proxy grid last-row geometry', () => {
  it.each([
    [0, 0],
    [1, 48],
    [2, 104],
    [3, 160],
  ])('uses the real virtualizer without a trailing gap for %s rows', async (count, height) => {
    await mount(Array.from({ length: count }, (_, index) => `node-${index}`))
    expect(virtualizer.value.getTotalSize()).toBe(height)
    expect(rows()).toHaveLength(count)
    expect(scroller.firstElementChild!.classList.contains('-mb-2')).toBe(false)
    for (const [index, row] of rows().entries()) {
      expect(row.offsetHeight).toBe(index === count - 1 ? 48 : 56)
    }
  })

  it('does not recenter a selected last card that is already fully visible', async () => {
    await mount(['first', 'last'], 'last')
    expect(scroller.querySelector('[data-node=last]')?.getAttribute('data-active')).toBe('true')
    // The engine may initialize its offset; no scroll is needed to align this visible card.
    expect(calls.scroll.mock.calls.every(([offset]) => offset === 0)).toBe(true)
    expect(scroller.scrollTop).toBe(0)
  })

  it('updates the last row after filtering and growing the list', async () => {
    await mount(['a', 'b', 'c'])
    nodes.value = ['a', 'b']
    await nextTick()
    await measureRows()
    expect(virtualizer.value.getTotalSize()).toBe(104)
    expect(rows().at(-1)!.offsetHeight).toBe(48)
    nodes.value = ['a', 'b', 'c', 'd']
    await nextTick()
    await measureRows()
    expect(virtualizer.value.getTotalSize()).toBe(216)
    expect(rows().at(-1)!.offsetHeight).toBe(48)
    expect(rows()[1]!.offsetHeight).toBe(56)
  })

  it('estimates an unmounted last row without adding a trailing gap', async () => {
    await mount(Array.from({ length: 100 }, (_, index) => `node-${index}`))
    expect(rows().length).toBeLessThan(100)
    expect(scroller.querySelector('[data-index="99"]')).toBeNull()
    expect(virtualizer.value.getTotalSize()).toBe(100 * 48 + 99 * 8)
  })

  it('recomputes rows and the last padding when width changes the column count', async () => {
    await mount(['a', 'b', 'c', 'd', 'e'])
    gridWidth.value = 350
    await nextTick()
    await measureRows()
    expect(rows()).toHaveLength(3)
    expect(virtualizer.value.getTotalSize()).toBe(160)
    expect(rows().map((row) => [...row.querySelectorAll('[data-node]')].map((node) => node.textContent)))
      .toEqual([['a', 'b'], ['c', 'd'], ['e']])
    expect(rows().at(-1)!.offsetHeight).toBe(48)
  })

  it('uses the large-card estimate and measured last height', async () => {
    proxyCardSize.value = PROXY_CARD_SIZE.LARGE
    geometry.cardHeight = 60
    await mount(['a', 'b'])
    expect(virtualizer.value.getTotalSize()).toBe(128)
    expect(rows().map((row) => row.offsetHeight)).toEqual([68, 60])
  })

  it('preserves node selection through the existing request wrapper', async () => {
    await mount(['a'])
    scroller.querySelector<HTMLButtonElement>('[data-node=a]')!.click()
    expect(calls.request).toHaveBeenCalledOnce()
    expect(calls.select).toHaveBeenCalledWith('group', 'a')
  })
})
