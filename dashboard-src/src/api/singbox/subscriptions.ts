import { StartedService } from '@/gen/daemon/started_service_pb'
import { serverStream } from './serverStream'
import { runStream, type StreamHandle } from './streams'

export type SubscriptionId = 'logs' | 'connections' | 'status' | 'groups' | 'outbounds'

const INTERVAL = 1_000_000_000n

const { method } = StartedService
const factories: Record<SubscriptionId, (signal: AbortSignal) => AsyncIterable<unknown>> = {
  logs: (signal) => serverStream(method.subscribeLog, {}, signal),
  connections: (signal) =>
    serverStream(method.subscribeConnections, { interval: INTERVAL }, signal),
  status: (signal) => serverStream(method.subscribeStatus, { interval: INTERVAL }, signal),
  groups: (signal) => serverStream(method.subscribeGroups, {}, signal),
  outbounds: (signal) => serverStream(method.subscribeOutbounds, {}, signal),
}

export const subscribeStream = <T>(id: SubscriptionId, onMessage: (msg: T) => void): StreamHandle =>
  runStream(factories[id], onMessage as (msg: unknown) => void)
