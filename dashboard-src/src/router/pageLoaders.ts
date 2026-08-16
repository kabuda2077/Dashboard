type PageImporter<T> = () => Promise<T>

export interface PaintEnvironment {
  requestFrame: (callback: () => void) => number
  cancelFrame: (handle: number) => void
}

export interface PagePreloadEnvironment {
  isVisible: () => boolean
  scheduleIdle: (callback: () => void) => number
  cancelIdle: (handle: number) => void
  addVisibilityListener: (listener: () => void) => void
  removeVisibilityListener: (listener: () => void) => void
}

const browserPaintEnvironment: PaintEnvironment = {
  requestFrame: (callback) => window.requestAnimationFrame(callback),
  cancelFrame: (handle) => window.cancelAnimationFrame(handle),
}

export const scheduleAfterInitialPaint = (
  callback: () => void,
  environment: PaintEnvironment = browserPaintEnvironment,
) => {
  let firstFrame = 0
  let secondFrame = 0
  let cancelled = false

  firstFrame = environment.requestFrame(() => {
    secondFrame = environment.requestFrame(() => {
      if (!cancelled) callback()
    })
  })

  return () => {
    cancelled = true
    environment.cancelFrame(firstFrame)
    if (secondFrame) environment.cancelFrame(secondFrame)
  }
}

export const createCachedPageLoader = <T>(importer: PageImporter<T>) => {
  let pending: Promise<T> | null = null

  return () => {
    pending ??= importer().catch((error) => {
      pending = null
      throw error
    })
    return pending
  }
}

export const createSequentialPagePreloader = (
  loaders: Array<() => Promise<unknown>>,
  environment: PagePreloadEnvironment,
) => {
  let nextIndex = 0
  let idleHandle: number | null = null
  let loading = false
  let started = false

  const scheduleNext = () => {
    if (
      !started ||
      loading ||
      idleHandle !== null ||
      nextIndex >= loaders.length ||
      !environment.isVisible()
    ) {
      return
    }

    idleHandle = environment.scheduleIdle(() => {
      idleHandle = null
      if (!started || !environment.isVisible()) return

      const loader = loaders[nextIndex]
      nextIndex += 1
      loading = true
      void loader()
        .catch(() => undefined)
        .finally(() => {
          loading = false
          scheduleNext()
        })
    })
  }

  const handleVisibilityChange = () => {
    if (!environment.isVisible()) {
      if (idleHandle !== null) {
        environment.cancelIdle(idleHandle)
        idleHandle = null
      }
      return
    }

    scheduleNext()
  }

  return {
    start() {
      if (started) return
      started = true
      environment.addVisibilityListener(handleVisibilityChange)
      scheduleNext()
    },
    stop() {
      if (!started) return
      started = false
      environment.removeVisibilityListener(handleVisibilityChange)
      if (idleHandle !== null) {
        environment.cancelIdle(idleHandle)
        idleHandle = null
      }
    },
  }
}

export const loadProxiesPage = createCachedPageLoader(() => import('@/views/ProxiesPage.vue'))
export const loadConnectionsPage = createCachedPageLoader(
  () => import('@/views/ConnectionsPage.vue'),
)
export const loadOverviewPage = createCachedPageLoader(() => import('@/views/OverviewPage.vue'))

const browserEnvironment: PagePreloadEnvironment = {
  isVisible: () => document.visibilityState === 'visible',
  scheduleIdle: (callback) => {
    if ('requestIdleCallback' in window) {
      return window.requestIdleCallback(callback, { timeout: 600 })
    }
    return globalThis.setTimeout(callback, 150)
  },
  cancelIdle: (handle) => {
    if ('cancelIdleCallback' in window) {
      window.cancelIdleCallback(handle)
      return
    }
    globalThis.clearTimeout(handle)
  },
  addVisibilityListener: (listener) => document.addEventListener('visibilitychange', listener),
  removeVisibilityListener: (listener) => document.removeEventListener('visibilitychange', listener),
}

const secondaryPagePreloader = createSequentialPagePreloader(
  [loadProxiesPage, loadConnectionsPage, loadOverviewPage],
  browserEnvironment,
)

export const preloadSecondaryPages = () => secondaryPagePreloader.start()
