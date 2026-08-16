import { ConnectionEventType } from '@/gen/daemon/started_service_pb'
import { describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

const streamCallbacks = vi.hoisted(() => new Map<string, (msg: unknown) => void>())

vi.mock('@/api/singbox/subscriptions', () => ({
  subscribeStream: vi.fn((id: string, onMessage: (msg: unknown) => void) => {
    streamCallbacks.set(id, onMessage)
    return { close: vi.fn() }
  }),
}))

vi.mock('@/composables/hostBridge', () => ({
  postHostMessage: vi.fn(),
}))

describe('sing-box stream mappings', () => {
  it('keeps closedAt connections out of the active snapshot', async () => {
    vi.useFakeTimers()
    const { fetchConnectionsAPI } = await import('@/assembly/connections/singbox')
    const stream = fetchConnectionsAPI()
    const onConnections = streamCallbacks.get('connections')

    expect(onConnections).toBeTypeOf('function')

    onConnections?.({
      reset: false,
      events: [
        {
          id: 'closed-on-arrival',
          type: ConnectionEventType.CONNECTION_EVENT_NEW,
          uplinkDelta: 0n,
          downlinkDelta: 0n,
          connection: {
            id: 'closed-on-arrival',
            closedAt: 1n,
            uplinkTotal: 10n,
            downlinkTotal: 20n,
          },
        },
      ],
    })

    vi.advanceTimersByTime(100)

    expect(stream.data.value?.active).toHaveLength(0)
    expect(stream.data.value?.closed).toHaveLength(1)
    stream.close()
    vi.useRealTimers()
  })

  it('maps sing-box traffic totals from the status stream', async () => {
    const { fetchTrafficAPI } = await import('@/assembly/overview/singbox')
    const stream = fetchTrafficAPI<{
      down: number
      up: number
      downTotal?: number
      upTotal?: number
    }>()
    const onStatus = streamCallbacks.get('status')

    expect(onStatus).toBeTypeOf('function')

    onStatus?.({
      downlink: 12n,
      uplink: 7n,
      downlinkTotal: 1200n,
      uplinkTotal: 700n,
    })
    await nextTick()

    expect(stream.data.value).toEqual({
      down: 12,
      up: 7,
      downTotal: 1200,
      upTotal: 700,
    })
    stream.close()
  })
})
