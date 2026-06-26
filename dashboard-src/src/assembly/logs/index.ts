// 组装层 · logs 门面。日志流按后端类型路由,统一返回 { data, close } 流。
import { isSingboxBackend } from '@/assembly/backend'
import * as clash from './clash'
import * as singbox from './singbox'

const backend = () => (isSingboxBackend.value ? singbox : clash)

export const fetchLogsAPI = <T>(params: Record<string, string> = {}) =>
  backend().fetchLogsAPI<T>(params)
