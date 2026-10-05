// 组装层 · Clash-compatible connections 门面。
import type { ConnectionsSnapshot } from './accessor'
import { connectionAccessor as clashConnectionAccessor } from './clash'

export type { ConnectionsSnapshot }

export { disconnectByIdAPI, disconnectAllAPI, fetchConnectionsAPI } from './clash'

// 当前后端的连接字段访问器(直接读取原始数据,不做 clash 形状化)。
export const connectionAccessor = () => clashConnectionAccessor

export { getConnectionDisplayValue, getConnectionVisibleSearchValues } from './clash'

// 连接封锁动作(Clash 专属),经 connections 域门面暴露给 view。
export { blockConnectionByIdAPI } from '@/api/clash'
