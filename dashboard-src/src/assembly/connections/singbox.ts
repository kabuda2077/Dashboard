// sing-box native 后端的连接组装:订阅 gRPC SubscribeConnections,把 protobuf 事件
// 维护成一张连接表,按 100ms 批量产出 { data, close } 流。
import { getSingboxClient } from '@/api/singbox/client'
import { runStream, type StreamHandle } from '@/api/singbox/streams'
import {
  ConnectionEventType,
  type Connection as PbConnection,
} from '@/gen/daemon/started_service_pb'
import type { Connection } from '@/types'
import { ref, type Ref } from 'vue'
import {
  createGetConnectionDisplayValue,
  createGetConnectionVisibleSearchValues,
  type ConnectionAccessor,
} from './accessor'

const SUBSCRIPTION_INTERVAL = 1_000_000_000n // 1s(ns)

interface SingboxStream<T> {
  data: Ref<T | undefined>
  close: () => void
}

const fetchSingboxConnections = <T>(): SingboxStream<T> => {
  const data = ref<T>()
  const client = getSingboxClient()?.client
  if (!client) return { data, close: () => {} }

  const conns = new Map<string, PbConnection>()
  let downloadTotal = 0
  let uploadTotal = 0
  let timer: ReturnType<typeof setTimeout> | null = null

  const emit = () => {
    timer = null
    data.value = {
      connections: Array.from(conns.values()),
      downloadTotal,
      uploadTotal,
      memory: 0,
    } as T
  }
  const scheduleEmit = () => {
    if (timer) return
    timer = setTimeout(emit, 100)
  }

  const handle: StreamHandle = runStream(
    (signal) => client.subscribeConnections({ interval: SUBSCRIPTION_INTERVAL }, { signal }),
    (msg) => {
      if (msg.reset) {
        conns.clear()
      }
      for (const event of msg.events) {
        uploadTotal += Number(event.uplinkDelta)
        downloadTotal += Number(event.downlinkDelta)

        switch (event.type) {
          case ConnectionEventType.CONNECTION_EVENT_NEW:
            if (event.connection) conns.set(event.id, event.connection)
            break
          case ConnectionEventType.CONNECTION_EVENT_UPDATE: {
            if (event.connection) {
              conns.set(event.id, event.connection)
            } else {
              const existing = conns.get(event.id)
              if (existing) {
                conns.set(event.id, {
                  ...existing,
                  uplinkTotal: existing.uplinkTotal + event.uplinkDelta,
                  downlinkTotal: existing.downlinkTotal + event.downlinkDelta,
                })
              }
            }
            break
          }
          case ConnectionEventType.CONNECTION_EVENT_CLOSED:
            conns.delete(event.id)
            break
        }
      }
      scheduleEmit()
    },
  )

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
