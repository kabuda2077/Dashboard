// 组装层 · connection 字段访问器。
// 每种后端(clash / sing-box native)各实现一份 ConnectionAccessor,直接从「原始数据」
// 读取/派生 view 需要的字段 —— 不再把 sing-box 塑造成 clash 形状。
// createGetConnectionDisplayValue 基于某一份 accessor 生成对应后端的 getConnectionDisplayValue,
// 由 index.ts 门面按当前后端动态选用。
import { CONNECTIONS_TABLE_ACCESSOR_KEY, PROXY_CHAIN_DIRECTION } from '@/constant'
import { getIPLabelFromMap } from '@/helper/sourceip'
import { fromNow, prettyBytesHelper } from '@/helper/utils'
import type { Connection } from '@/types'
import * as ipaddr from 'ipaddr.js'

export type ConnectionDisplayOptions = {
  mode: 'card' | 'table'
  proxyChainDirection: PROXY_CHAIN_DIRECTION | string
  showFullProxyChain: boolean
}

// 各后端原始数据 → view 字段的读取契约。实现内部按各自后端的原始类型取值。
export interface ConnectionAccessor {
  chains(connection: Connection): string[]
  download(connection: Connection): number
  upload(connection: Connection): number
  start(connection: Connection): string | number
  rule(connection: Connection): string
  rulePayload(connection: Connection): string
  sourceIP(connection: Connection): string
  sourcePort(connection: Connection): string
  network(connection: Connection): string
  networkType(connection: Connection): string
  // 目的地主机名,裸值(无端口、无 IPv6 方括号),供聚合/分组按主机归类。
  hostname(connection: Connection): string
  // 目的地 `host:port`(IPv6 加方括号),供展示。
  host(connection: Connection): string
  process(connection: Connection): string
  destination(connection: Connection): string
  inboundUser(connection: Connection): string
  sniffHost(connection: Connection): string
  remoteAddress(connection: Connection): string
  // 以下三项为 sing-box 原生字段;clash 无对应数据,返回空串(展示为 '-')。
  protocol(connection: Connection): string
  outboundType(connection: Connection): string
  fromOutbound(connection: Connection): string
  // 仅 clash 支持的 smart 降级标记;sing-box 返回 undefined。
  smartBlock(connection: Connection): string | undefined
}

const getDestinationType = (destination: string) => {
  if (ipaddr.IPv4.isIPv4(destination)) {
    return 'IPv4'
  } else if (ipaddr.IPv6.isIPv6(destination)) {
    return 'IPv6'
  } else {
    return 'FQDN'
  }
}

const getVisibleChains = (
  accessor: ConnectionAccessor,
  connection: Connection,
  options: ConnectionDisplayOptions,
) => {
  let chains = accessor.chains(connection)

  if ((options.mode === 'card' || !options.showFullProxyChain) && chains.length > 2) {
    chains = [chains[0], chains[chains.length - 1]]
  }

  return options.proxyChainDirection === PROXY_CHAIN_DIRECTION.REVERSE
    ? chains
    : [...chains].reverse()
}

export const createGetConnectionDisplayValue =
  (accessor: ConnectionAccessor) =>
  (
    connection: Connection,
    key: CONNECTIONS_TABLE_ACCESSOR_KEY,
    options: ConnectionDisplayOptions,
  ) => {
    switch (key) {
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Type:
        return accessor.networkType(connection)
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Process:
        return accessor.process(connection)
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Host:
        return accessor.host(connection)
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Rule:
        return accessor.rule(connection)
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Chains:
        return getVisibleChains(accessor, connection, options).join(' → ')
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Outbound:
        return accessor.chains(connection)[0] || ''
      case CONNECTIONS_TABLE_ACCESSOR_KEY.DlSpeed:
        return `${prettyBytesHelper(connection.downloadSpeed)}/s`
      case CONNECTIONS_TABLE_ACCESSOR_KEY.UlSpeed:
        return `${prettyBytesHelper(connection.uploadSpeed)}/s`
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Download:
        return prettyBytesHelper(accessor.download(connection))
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Upload:
        return prettyBytesHelper(accessor.upload(connection))
      case CONNECTIONS_TABLE_ACCESSOR_KEY.ConnectTime:
        return fromNow(accessor.start(connection))
      case CONNECTIONS_TABLE_ACCESSOR_KEY.SourceIP:
        return getIPLabelFromMap(accessor.sourceIP(connection))
      case CONNECTIONS_TABLE_ACCESSOR_KEY.SourcePort:
        return accessor.sourcePort(connection)
      case CONNECTIONS_TABLE_ACCESSOR_KEY.SniffHost:
        return accessor.sniffHost(connection) || '-'
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Destination:
        return accessor.destination(connection)
      case CONNECTIONS_TABLE_ACCESSOR_KEY.DestinationType:
        return getDestinationType(accessor.destination(connection))
      case CONNECTIONS_TABLE_ACCESSOR_KEY.RemoteAddress:
        return accessor.remoteAddress(connection) || '-'
      case CONNECTIONS_TABLE_ACCESSOR_KEY.InboundUser:
        return accessor.inboundUser(connection)
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Protocol:
        return accessor.protocol(connection) || '-'
      case CONNECTIONS_TABLE_ACCESSOR_KEY.OutboundType:
        return accessor.outboundType(connection) || '-'
      case CONNECTIONS_TABLE_ACCESSOR_KEY.FromOutbound:
        return accessor.fromOutbound(connection) || '-'
      case CONNECTIONS_TABLE_ACCESSOR_KEY.Close:
        return ''
    }
  }

export const createGetConnectionVisibleSearchValues =
  (accessor: ConnectionAccessor) =>
  (
    connection: Connection,
    keys: CONNECTIONS_TABLE_ACCESSOR_KEY[],
    options: ConnectionDisplayOptions,
  ) => {
    const getDisplayValue = createGetConnectionDisplayValue(accessor)

    return keys
      .filter((key) => key !== CONNECTIONS_TABLE_ACCESSOR_KEY.Close)
      .map((key) => getDisplayValue(connection, key, options))
  }
