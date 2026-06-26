// Clash WS 后端的连接流、断连动作,以及「原始 Clash 连接数据 → view 字段」的访问器。
import { createClashWebSocket, disconnectAllClashAPI, disconnectClashByIdAPI } from '@/api/clash'
import { proxyMap } from '@/assembly/proxies'
import { PROXY_TYPE } from '@/constant'
import type { ClashConnectionRawMessage, Connection } from '@/types'
import { head } from 'lodash'
import {
  createGetConnectionDisplayValue,
  createGetConnectionVisibleSearchValues,
  type ConnectionAccessor,
} from './accessor'

export const disconnectByIdAPI = disconnectClashByIdAPI

export const disconnectAllAPI = disconnectAllClashAPI

export const fetchConnectionsAPI = <T>() => createClashWebSocket<T>('connections')

const asClash = (connection: Connection) => connection as ClashConnectionRawMessage

const getNetwork = (c: ClashConnectionRawMessage) => {
  const { destinationPort, sniffHost, network } = c.metadata

  if ((destinationPort === '443' || sniffHost) && network === 'udp') {
    return 'quic'
  }

  return network
}

const getHostname = (c: ClashConnectionRawMessage) =>
  c.metadata.host || c.metadata.sniffHost || c.metadata.destinationIP

export const connectionAccessor: ConnectionAccessor = {
  chains: (connection) => asClash(connection).chains,
  download: (connection) => asClash(connection).download,
  upload: (connection) => asClash(connection).upload,
  start: (connection) => asClash(connection).start,
  rule: (connection) => {
    const clash = asClash(connection)

    return clash.rulePayload ? `${clash.rule}: ${clash.rulePayload}` : clash.rule
  },
  rulePayload: (connection) => asClash(connection).rulePayload,
  sourceIP: (connection) => asClash(connection).metadata.sourceIP,
  sourcePort: (connection) => asClash(connection).metadata.sourcePort,
  network: (connection) => getNetwork(asClash(connection)),
  networkType: (connection) => {
    const clash = asClash(connection)

    return `${clash.metadata.type} | ${getNetwork(clash)}`
  },
  hostname: (connection) => getHostname(asClash(connection)),
  host: (connection) => {
    const clash = asClash(connection)
    const host = getHostname(clash)

    if (host.includes(':')) {
      return `[${host}]:${clash.metadata.destinationPort}`
    }
    return `${host}:${clash.metadata.destinationPort}`
  },
  process: (connection) => {
    const { metadata } = asClash(connection)

    return metadata.process || metadata.processPath.replace(/^.*[/\\](.*)$/, '$1') || '-'
  },
  destination: (connection) => {
    const clash = asClash(connection)
    const finalProxyType = proxyMap.value[head(clash.chains) || '']?.type.toLowerCase()

    if (finalProxyType === PROXY_TYPE.Direct && clash.metadata.remoteDestination) {
      return clash.metadata.remoteDestination
    }

    return clash.metadata.destinationIP || clash.metadata.host
  },
  inboundUser: (connection) => {
    const { metadata } = asClash(connection)

    return metadata.inboundUser || metadata.inboundName || metadata.inboundPort || '-'
  },
  sniffHost: (connection) => asClash(connection).metadata.sniffHost,
  remoteAddress: (connection) => asClash(connection).metadata.remoteDestination,
  // clash 不提供这些字段。
  protocol: () => '',
  outboundType: () => '',
  fromOutbound: () => '',
  smartBlock: (connection) => asClash(connection).metadata.smartBlock,
}

export const getConnectionDisplayValue = createGetConnectionDisplayValue(connectionAccessor)

export const getConnectionVisibleSearchValues =
  createGetConnectionVisibleSearchValues(connectionAccessor)
