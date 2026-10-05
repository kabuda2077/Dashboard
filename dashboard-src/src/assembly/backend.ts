// 组装层 · 桌面后端能力门控。
// mihomo 与 sing-box 都通过 Clash-compatible REST/WS API 接入桌面面板。

import { probeClashChannel } from '@/api/clash'
import { activeBackend } from '@/store/setup'
import type { Backend } from '@/types'
export const isBackendAvailable = (backend: Backend, timeout: number = 10000) =>
  probeClashChannel(backend, timeout)
