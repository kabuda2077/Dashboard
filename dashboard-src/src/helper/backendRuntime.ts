import { resetConfigs, startConfigRuntime, stopConfigRuntime } from '@/assembly/config'
import { initLogs, resetLogs, stopLogs } from '@/assembly/logs'
import { fetchProxies, resetProxies } from '@/assembly/proxies'
import { fetchRules, resetRules } from '@/assembly/rules'
import { closeProxyGroupChain } from '@/composables/proxyGroupChain'
import { initConnections, resetConnections, stopConnections } from '@/store/connections'
import { initSatistic, resetStatistics, stopSatistic } from '@/store/overview'
import { smartOrderMap, smartWeightsMap } from '@/store/smart'
import { onScopeDispose, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { backendSessionGeneration, backendSessionReady } from './backendSession'

export const resetBackendRuntime = () => {
  resetConnections()
  resetLogs()
  resetStatistics()
  resetConfigs()
  resetProxies()
  resetRules()
  smartOrderMap.value = {}
  smartWeightsMap.value = {}
  closeProxyGroupChain()
}

// Called once by the root App. Views consume stores; they do not own global streams.
export const startBackendRuntime = (route: MaybeRefOrGetter<unknown>) => {
  const started = new Set<string>()
  const pending = new Map<string, Promise<unknown>>()
  const ensure = (key: string, action: () => unknown) => {
    if (!backendSessionReady.value || started.has(key) || pending.has(key)) return
    started.add(key)
    const epoch = backendSessionGeneration.value
    const task: Promise<unknown> = Promise.resolve()
      .then(() => {
        if (
          pending.get(key) === task &&
          epoch === backendSessionGeneration.value &&
          backendSessionReady.value
        )
          return action()
      })
      .catch(() => {
        if (pending.get(key) === task) started.delete(key)
      })
      .finally(() => {
        if (pending.get(key) === task) pending.delete(key)
      })
    pending.set(key, task)
  }
  const ensureSessionData = () => {
    ensure('connections', initConnections)
    ensure('rules', fetchRules)
    ensure('proxies', fetchProxies)
    ensure('statistics', initSatistic)
    ensure('logs', initLogs)
  }
  const stopSession = watch(
    [backendSessionGeneration, backendSessionReady],
    () => {
      started.clear()
      pending.clear()
      resetBackendRuntime()
      if (!backendSessionReady.value) return
      startConfigRuntime()
      ensureSessionData()
    },
    { immediate: true, flush: 'sync' },
  )
  const stopRoute = watch(
    () => toValue(route),
    (name, previous) => {
      // Re-entering proxies refreshes the snapshot, but reuses any in-flight load.
      if (name === 'proxies') {
        if (previous !== undefined && !pending.has('proxies')) started.delete('proxies')
        ensure('proxies', fetchProxies)
      }
    },
  )
  const visible = () => {
    if (document.visibilityState !== 'hidden' && backendSessionReady.value) {
      ensureSessionData()
    }
  }
  document.addEventListener('visibilitychange', visible)
  const stop = () => {
    stopSession()
    stopRoute()
    pending.clear()
    document.removeEventListener('visibilitychange', visible)
    stopConfigRuntime()
    stopConnections()
    stopLogs()
    stopSatistic()
  }
  onScopeDispose(stop)
  return stop
}
