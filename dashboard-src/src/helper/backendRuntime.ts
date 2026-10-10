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
  let frame = 0
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
  const routeData = () => {
    switch (toValue(route)) {
      case 'rules':
        ensure('rules', fetchRules)
        break
      case 'logs':
        ensure('logs', initLogs)
        break
      case 'proxies':
        ensure('proxies', fetchProxies)
        break
      case 'overview':
        ensure('proxies', fetchProxies)
        ensure('statistics', initSatistic)
        break
    }
  }
  const deferred = () => {
    cancelAnimationFrame(frame)
    frame = requestAnimationFrame(() => {
      if (document.visibilityState !== 'hidden') {
        ensure('proxies', fetchProxies)
        ensure('statistics', initSatistic)
      }
    })
  }
  const stopSession = watch(
    [backendSessionGeneration, backendSessionReady],
    () => {
      cancelAnimationFrame(frame)
      started.clear()
      pending.clear()
      resetBackendRuntime()
      if (!backendSessionReady.value) return
      startConfigRuntime()
      ensure('connections', initConnections)
      routeData()
      deferred()
    },
    { immediate: true, flush: 'sync' },
  )
  const stopRoute = watch(
    () => toValue(route),
    (name, previous) => {
      // Re-entering proxies refreshes the snapshot, but reuses any in-flight load.
      if (name === 'proxies' && previous !== undefined && !pending.has('proxies'))
        started.delete('proxies')
      routeData()
    },
  )
  const visible = () => {
    if (document.visibilityState !== 'hidden' && backendSessionReady.value) {
      routeData()
      deferred()
    }
  }
  document.addEventListener('visibilitychange', visible)
  const stop = () => {
    stopSession()
    stopRoute()
    pending.clear()
    cancelAnimationFrame(frame)
    document.removeEventListener('visibilitychange', visible)
    stopConfigRuntime()
    stopConnections()
    stopLogs()
    stopSatistic()
  }
  onScopeDispose(stop)
  return stop
}
