// 组装层 · Clash-compatible connections 门面。
import { CONNECTIONS_TABLE_ACCESSOR_KEY } from '@/constant'
import type { Connection } from '@/types'
import type { ConnectionDisplayOptions, ConnectionsSnapshot } from './accessor'
import * as clash from './clash'

export type { ConnectionsSnapshot }

export const disconnectByIdAPI = (id: string) => clash.disconnectByIdAPI(id)

export const disconnectAllAPI = () => clash.disconnectAllAPI()

export const fetchConnectionsAPI = () => clash.fetchConnectionsAPI()

// 当前后端的连接字段访问器(直接读取原始数据,不做 clash 形状化)。
export const connectionAccessor = () => clash.connectionAccessor

// 动态选用当前后端的 getConnectionDisplayValue。
export const getConnectionDisplayValue = (
  connection: Connection,
  key: CONNECTIONS_TABLE_ACCESSOR_KEY,
  options: ConnectionDisplayOptions,
) => clash.getConnectionDisplayValue(connection, key, options)

export const getConnectionVisibleSearchValues = (
  connection: Connection,
  keys: CONNECTIONS_TABLE_ACCESSOR_KEY[],
  options: ConnectionDisplayOptions,
) => clash.getConnectionVisibleSearchValues(connection, keys, options)

// 连接封锁动作(Clash 专属),经 connections 域门面暴露给 view。
export { blockConnectionByIdAPI } from '@/api/clash'
