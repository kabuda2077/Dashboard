// Clash WS 后端的日志流。
import { createClashWebSocket } from '@/api/clash'

export const fetchLogsAPI = <T>(params: Record<string, string> = {}) =>
  createClashWebSocket<T>('logs', params)
