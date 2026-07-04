// sing-box native 后端的连接组装:订阅 gRPC SubscribeConnections,把 protobuf 事件
// 维护成一张连接表,按 100ms 批量产出 { data, close } 流。
import { getSingboxClient } from '@/api/singbox/client'
import { subscribeStream } from '@/api/singbox/subscriptions'
import { postHostMessage } from '@/composables/hostBridge'
import {
  ConnectionEventType,
  type ConnectionEvents,
  type Connection as PbConnection,
} from '@/gen/daemon/started_service_pb'
import type { Connection } from '@/types'
import { ref, type Ref } from 'vue'
import {
  createGetConnectionDisplayValue,
  createGetConnectionVisibleSearchValues,
  type ConnectionAccessor,
  type ConnectionsSnapshot,
} from './accessor'

const fetchSingboxConnections = (): {
  data: Ref<ConnectionsSnapshot | undefined>
  close: () => void
} => {
  const data = ref<ConnectionsSnapshot>()
  const conns = new Map<string, Connection>()
  let newlyClosed: Connection[] = []
  let timer: ReturnType<typeof setTimeout> | null = null
  const startedAt = performance.now()
  let firstMessage = true

  const enrich = (c: PbConnection | Connection, down: number, up: number): Connection =>
    Object.assign({}, c, { downloadSpeed: down, uploadSpeed: up }) as Connection

  const close = (id: string, base?: PbConnection | Connection) => {
    const c = base ?? conns.get(id)
    conns.delete(id)
    if (c) newlyClosed.push(enrich(c, 0, 0))
  }

  const emit = () => {
    timer = null
    data.value = {
      active: Array.from(conns.values()),
      closed: newlyClosed,
    }
    newlyClosed = []
  }
  const scheduleEmit = () => {
    if (timer) return
    timer = setTimeout(emit, 100)
  }

  const handle = subscribeStream<ConnectionEvents>('connections', (msg) => {
    if (firstMessage) {
      firstMessage = false
      postHostMessage({
        type: 'performance',
        name: 'singbox:connections:firstMessage',
        durationMs: Math.round(performance.now() - startedAt),
      })
    }
    if (msg.reset) {
      conns.clear()
    }
    for (const event of msg.events) {
      const downDelta = Number(event.downlinkDelta)
      const upDelta = Number(event.uplinkDelta)

      switch (event.type) {
        case ConnectionEventType.CONNECTION_EVENT_NEW:
          if (event.connection) {
            if (event.connection.closedAt > 0n) close(event.id, event.connection)
            else conns.set(event.id, enrich(event.connection, 0, 0))
          }
          break
        case ConnectionEventType.CONNECTION_EVENT_UPDATE: {
          if (event.connection) {
            if (event.connection.closedAt > 0n) close(event.id, event.connection)
            else conns.set(event.id, enrich(event.connection, downDelta, upDelta))
          } else {
            const prev = conns.get(event.id)
            if (prev) {
              const s = asSingbox(prev)
              conns.set(
                event.id,
                enrich(
                  {
                    ...s,
                    uplinkTotal: s.uplinkTotal + event.uplinkDelta,
                    downlinkTotal: s.downlinkTotal + event.downlinkDelta,
                  },
                  downDelta,
                  upDelta,
                ),
              )
            }
          }
          break
        }
        case ConnectionEventType.CONNECTION_EVENT_CLOSED:
          close(event.id, event.connection)
          break
      }
    }
    scheduleEmit()
  })

  return {
    data,
    close: () => {
      if (timer) clearTimeout(timer)
      handle.close()
    },
  }
}

const closeSingboxConnection = async (id: string) => {
  const client = getSingboxClient()?.client
  if (!client) return
  await client.closeConnection({ id })
}

const closeAllSingboxConnections = async () => {
  const client = getSingboxClient()?.client
  if (!client) return
  await client.closeAllConnections({})
}

export const disconnectByIdAPI = closeSingboxConnection

export const disconnectAllAPI = closeAllSingboxConnections

export const fetchConnectionsAPI = fetchSingboxConnections

// 拆分 "ip:port" / "[ipv6]:port"
const splitHostPort = (value: string): [string, string] => {
  if (!value) return ['', '']
  const idx = value.lastIndexOf(':')
  if (idx === -1) return [value, '']

  let host = value.slice(0, idx)
  const port = value.slice(idx + 1)

  if (host.startsWith('[') && host.endsWith(']')) {
    host = host.slice(1, -1)
  }

  return [host, port]
}

const asSingbox = (connection: Connection) => connection as PbConnection

const getNetwork = (c: PbConnection) => {
  const [, destinationPort] = splitHostPort(c.destination)

  if ((destinationPort === '443' || c.domain) && c.network === 'udp') {
    return 'quic'
  }

  return c.network
}

const getHostname = (c: PbConnection) => c.domain || splitHostPort(c.destination)[0]

export const connectionAccessor: ConnectionAccessor = {
  chains: (connection) => {
    const c = asSingbox(connection)

    return c.chainList.length ? c.chainList : [c.outbound].filter(Boolean)
  },
  download: (connection) => Number(asSingbox(connection).downlinkTotal),
  upload: (connection) => Number(asSingbox(connection).uplinkTotal),
  start: (connection) => Number(asSingbox(connection).createdAt),
  rule: (connection) => asSingbox(connection).rule,
  rulePayload: () => '',
  sourceIP: (connection) => splitHostPort(asSingbox(connection).source)[0],
  sourcePort: (connection) => splitHostPort(asSingbox(connection).source)[1],
  network: (connection) => getNetwork(asSingbox(connection)),
  networkType: (connection) => {
    const c = asSingbox(connection)

    return `${c.inboundType} | ${getNetwork(c)}`
  },
  hostname: (connection) => getHostname(asSingbox(connection)),
  host: (connection) => {
    const c = asSingbox(connection)
    const [, destinationPort] = splitHostPort(c.destination)
    const host = getHostname(c)

    if (host.includes(':')) {
      return `[${host}]:${destinationPort}`
    }
    return `${host}:${destinationPort}`
  },
  process: (connection) => {
    const processPath = asSingbox(connection).processInfo?.processPath ?? ''

    return processPath.replace(/^.*[/\\](.*)$/, '$1') || '-'
  },
  destination: (connection) => {
    const c = asSingbox(connection)

    return splitHostPort(c.destination)[0] || c.domain
  },
  inboundUser: (connection) => {
    const c = asSingbox(connection)

    return c.user || c.inbound || '-'
  },
  sniffHost: (connection) => asSingbox(connection).domain,
  remoteAddress: (connection) => asSingbox(connection).destination,
  protocol: (connection) => asSingbox(connection).protocol,
  outboundType: (connection) => asSingbox(connection).outboundType,
  fromOutbound: (connection) => asSingbox(connection).fromOutbound,
  smartBlock: () => undefined,
}

export const getConnectionDisplayValue = createGetConnectionDisplayValue(connectionAccessor)

export const getConnectionVisibleSearchValues =
  createGetConnectionVisibleSearchValues(connectionAccessor)
