// 组装层 · 桌面后端能力门控。
// mihomo 与 sing-box 都通过 Clash-compatible REST/WS API 接入桌面面板。

import { probeClashChannel } from '@/api/clash'
import type { Backend } from '@/types'

export const probeBackend = (backend: Backend, timeout: number = 10000, signal?: AbortSignal) =>
  probeClashChannel(backend, timeout, signal)
export const isBackendAvailable = (backend: Backend, timeout: number = 10000) =>
  probeBackend(backend, timeout).then((result) => result.ok)
