import { afterEach, expect, it, vi } from 'vitest'
import { addBackend } from '@/store/setup'
import { applyHostState, applyHostRuntimeState } from '@/composables/hostBridge'
import { createClashWebSocket } from '@/api/clash'

const sockets = vi.hoisted(() => [] as { onmessage: (event: { data: string }) => void; close: ReturnType<typeof vi.fn> }[])
vi.mock('reconnectingwebsocket', () => ({ default: class {
  onmessage = () => {}
  close = vi.fn()
  constructor() { sockets.push(this) }
} }))
afterEach(() => { vi.useRealTimers(); sockets.length = 0 })

it('drops buffered packets after close and old-session packets before consumer cleanup', () => {
  vi.useFakeTimers()
  addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', password: '', secondaryPath: '' }, { replaceExisting: true })
  applyHostState({ coreType: 'mihomo', processId: 1, isRunning: true })
  const stream = createClashWebSocket<{ down: number }>('traffic')
  sockets[0].onmessage({ data: '{"down":5}' })
  stream.close()
  vi.advanceTimersByTime(150)
  expect(stream.data.value).toBeUndefined()
  expect(sockets[0].close).toHaveBeenCalledTimes(1)
  const next = createClashWebSocket<{ down: number }>('traffic')
  sockets[1].onmessage({ data: '{"down":6}' })
  applyHostRuntimeState({ processId: 2 })
  vi.advanceTimersByTime(150)
  expect(next.data.value).toBeUndefined()
  next.close()
})
