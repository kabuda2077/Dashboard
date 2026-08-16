import {
  createCachedPageLoader,
  createSequentialPagePreloader,
  scheduleAfterInitialPaint,
  type PaintEnvironment,
  type PagePreloadEnvironment,
} from '@/router/pageLoaders'
import { describe, expect, it, vi } from 'vitest'

const flushPromises = () => new Promise((resolve) => setTimeout(resolve, 0))

const createEnvironment = (visible = true) => {
  let isVisible = visible
  let visibilityListener: (() => void) | null = null
  let nextHandle = 1
  const callbacks = new Map<number, () => void>()

  const environment: PagePreloadEnvironment = {
    isVisible: () => isVisible,
    scheduleIdle: (callback) => {
      const handle = nextHandle++
      callbacks.set(handle, callback)
      return handle
    },
    cancelIdle: (handle) => callbacks.delete(handle),
    addVisibilityListener: (listener) => {
      visibilityListener = listener
    },
    removeVisibilityListener: () => {
      visibilityListener = null
    },
  }

  return {
    environment,
    runNextIdle() {
      const entry = callbacks.entries().next().value as [number, () => void] | undefined
      if (!entry) return false
      callbacks.delete(entry[0])
      entry[1]()
      return true
    },
    setVisible(value: boolean) {
      isVisible = value
      visibilityListener?.()
    },
    scheduledCount: () => callbacks.size,
  }
}

describe('page loaders', () => {
  it('waits for two animation frames before starting background work', () => {
    const callbacks = new Map<number, () => void>()
    let nextHandle = 1
    const environment: PaintEnvironment = {
      requestFrame: (callback) => {
        const handle = nextHandle++
        callbacks.set(handle, callback)
        return handle
      },
      cancelFrame: (handle) => callbacks.delete(handle),
    }
    const callback = vi.fn()

    scheduleAfterInitialPaint(callback, environment)
    const first = callbacks.entries().next().value as [number, () => void]
    callbacks.delete(first[0])
    first[1]()
    expect(callback).not.toHaveBeenCalled()

    const second = callbacks.entries().next().value as [number, () => void]
    callbacks.delete(second[0])
    second[1]()
    expect(callback).toHaveBeenCalledOnce()
  })

  it('deduplicates concurrent imports and retries after a failure', async () => {
    const importer = vi
      .fn<() => Promise<string>>()
      .mockRejectedValueOnce(new Error('load failed'))
      .mockResolvedValue('loaded')
    const load = createCachedPageLoader(importer)

    const first = load()
    expect(load()).toBe(first)
    await expect(first).rejects.toThrow('load failed')
    await expect(load()).resolves.toBe('loaded')
    expect(importer).toHaveBeenCalledTimes(2)
  })

  it('preloads sequentially and continues after a failed page', async () => {
    const calls: string[] = []
    const { environment, runNextIdle } = createEnvironment()
    const preloader = createSequentialPagePreloader(
      [
        async () => calls.push('proxies'),
        async () => {
          calls.push('connections')
          throw new Error('ignored')
        },
        async () => calls.push('overview'),
      ],
      environment,
    )

    preloader.start()
    expect(runNextIdle()).toBe(true)
    await flushPromises()
    expect(calls).toEqual(['proxies'])
    expect(runNextIdle()).toBe(true)
    await flushPromises()
    expect(calls).toEqual(['proxies', 'connections'])
    expect(runNextIdle()).toBe(true)
    await flushPromises()
    expect(calls).toEqual(['proxies', 'connections', 'overview'])
  })

  it('pauses pending work while hidden and resumes when visible', async () => {
    const load = vi.fn(async () => undefined)
    const { environment, runNextIdle, scheduledCount, setVisible } = createEnvironment()
    const preloader = createSequentialPagePreloader([load], environment)

    preloader.start()
    expect(scheduledCount()).toBe(1)
    setVisible(false)
    expect(scheduledCount()).toBe(0)
    expect(runNextIdle()).toBe(false)
    expect(load).not.toHaveBeenCalled()

    setVisible(true)
    expect(runNextIdle()).toBe(true)
    await flushPromises()
    expect(load).toHaveBeenCalledOnce()
  })
})
