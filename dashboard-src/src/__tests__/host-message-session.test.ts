import { afterEach, expect, it, vi } from 'vitest'
import { mergeHostDraft } from '@/helper/hostDraft'

afterEach(() => { Reflect.deleteProperty(window, 'chrome') })

it('shares one native receiver and applies runtime state once across subscribers', async () => {
  vi.resetModules()
  let receive: (event: MessageEvent) => void = () => {}
  const add = vi.fn((_type, listener) => { receive = listener })
  const remove = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: vi.fn(), addEventListener: add, removeEventListener: remove,
  } } })
  const bridge = await import('@/composables/hostBridge')
  const first = vi.fn(), second = vi.fn()
  const stopFirst = bridge.addHostMessageListener(first)
  const stopSecond = bridge.addHostMessageListener(second)
  expect(add).toHaveBeenCalledTimes(1)
  const state = { apiUrl: 'http://localhost:9090', secret: 'one', coreType: 'mihomo', processId: 1, isRunning: true }
  receive(new MessageEvent('message', { data: { type: 'state', state } }))
  const generation = bridge.hostSessionGeneration.value
  receive(new MessageEvent('message', { data: { type: 'state', state: { ...state, latestCoreVersion: 'updated' } } }))
  expect(bridge.hostSessionGeneration.value).toBe(generation)
  receive(new MessageEvent('message', { data: { type: 'runtimeState', runtimeState: { processId: 2 } } }))
  expect(bridge.hostSessionGeneration.value).toBe(generation + 1)
  expect(first).toHaveBeenCalledTimes(3)
  expect(second).toHaveBeenCalledTimes(3)
  stopFirst()
  expect(remove).not.toHaveBeenCalled()
  stopSecond()
  expect(remove).toHaveBeenCalledTimes(1)
})

it('keeps dirty fields while refreshing clean fields and accepts acknowledged edits', () => {
  const baseline = { secret: 'old', path: 'original' }
  const draft = { secret: 'typing', path: 'original' }
  const incoming = { secret: 'old', path: 'updated' }
  expect(mergeHostDraft(draft, baseline, incoming)).toEqual({ secret: 'typing', path: 'updated' })
  expect(mergeHostDraft(draft, baseline, { ...incoming, secret: 'typing' }).secret).toBe('typing')
  expect(mergeHostDraft(draft, draft, { secret: 'remote', path: 'remote' })).toEqual({ secret: 'remote', path: 'remote' })
})

it('retains backend identity when the desktop changes only its credential', async () => {
  vi.resetModules()
  localStorage.clear()
  const setup = await import('@/store/setup')
  const backend = { type: 'clash' as const, protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: 'old' }
  setup.addBackend(backend, { replaceExisting: true })
  const id = setup.activeUuid.value
  setup.addBackend({ ...backend, password: 'new' }, { replaceExisting: true })
  expect(setup.activeUuid.value).toBe(id)
  expect(setup.activeBackend.value?.password).toBe('new')
})
