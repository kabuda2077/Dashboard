// 组装层 · 桌面后端能力门控。
// mihomo 与 sing-box 都通过 Clash-compatible REST/WS API 接入桌面面板。

import { probeClashChannel } from '@/api/clash'
import { activeBackend } from '@/store/setup'
import type { Backend } from '@/types'
import { computed } from 'vue'

// 桌面注入的后端始终为 Clash-compatible API；核心升级由 C# 宿主管理。
export const capabilities = computed(() => ({
  proxies: !!activeBackend.value,
  connections: !!activeBackend.value,
  logs: !!activeBackend.value,
  overview: !!activeBackend.value,
  rules: !!activeBackend.value,
  providers: !!activeBackend.value,
  dns: !!activeBackend.value,
  smart: !!activeBackend.value,
  upgrade: !!activeBackend.value,
  tools: false,
}))

export const isBackendAvailable = (backend: Backend, timeout: number = 10000) =>
  probeClashChannel(backend, timeout)
