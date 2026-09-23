import { afterEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
let stop: (() => void) | undefined

afterEach(() => {
  stop?.()
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})

it('saves one burst of preference edits and does no work while idle', async () => {
  vi.useFakeTimers()
  let receive: (event: MessageEvent) => void = () => {}
  let saves = 0
  Object.defineProperty(window, 'chrome', {
    configurable: true,
    value: {
      webview: {
        postMessage: (message: { type: string; requestId: string }) => {
          if (message.type !== 'saveDashboardSettings') return
          saves++
          queueMicrotask(() =>
            receive(
              new MessageEvent('message', {
                data: {
                  type: 'dashboardSettingsSaved',
                  requestId: message.requestId,
                  success: true,
                },
              }),
            ),
          )
        },
        addEventListener: (_: string, listener: typeof receive) => {
          receive = listener
        },
        removeEventListener: vi.fn(),
      },
    },
  })
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  const { useDashboardStorage } = await import('@/helper/storage')
  const { applyHostState } = await import('@/composables/hostBridge')
  // Production startup restores the host snapshot before installing sync.
  applyHostState({ dashboardSettings: {} })
  stop = installDashboardSettingsSync()
  for (let index = 0; index < 100; index++) {
    applyHostState({
      coreType: 'mihomo',
      apiUrl: 'http://localhost:9090',
      isRunning: true,
      processId: 1,
      latestCoreVersion: `notice-${index}`,
      dashboardSettings: {},
    })
    await nextTick()
  }
  await vi.advanceTimersByTimeAsync(1300)
  expect(saves).toBe(0)
  const preference = useDashboardStorage('config/p9-measurement', 0)
  for (let index = 1; index <= 100; index++) {
    preference.value = index
    await nextTick()
  }
  await vi.advanceTimersByTimeAsync(301)
  const afterDebounce = saves
  await vi.advanceTimersByTimeAsync(3000)
  console.info(`P9 settings writes: edits=100 afterDebounce=${afterDebounce} afterIdle=${saves}`)
  expect(afterDebounce).toBe(1)
  expect(saves).toBe(1)
})
