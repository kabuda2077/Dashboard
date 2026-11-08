import { resetConfigs, startConfigRuntime, stopConfigRuntime } from '@/assembly/config'
import { initLogs, resetLogs, stopLogs } from '@/assembly/logs'
import { fetchProxies, resetProxies } from '@/assembly/proxies'
import { fetchRules, resetRules } from '@/assembly/rules'
import { closeProxyGroupChain } from '@/helper/proxy-group-chain'
import { initConnections, resetConnections, stopConnections } from '@/store/connections'
import { initSatistic, resetStatistics, stopSatistic } from '@/store/overview'
import { smartOrderMap, smartWeightsMap } from '@/store/smart'
import { onScopeDispose, toValue, watch, type MaybeRefOrGetter } from 'vue'
import {
  backendSessionGeneration,
  backendSessionReady,
  captureBackendSession,
} from './backendSession'
import { notifyRequestErrorForSession } from './request-error'

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
  let rulesAttempt = 0
  const ensure = (key: string, action: () => unknown) => {
    if (!backendSessionReady.value || started.has(key) || pending.has(key)) return
    started.add(key)
    const epoch = backendSessionGeneration.value
    const session = captureBackendSession()
    const attempt = key === 'rules' ? ++rulesAttempt : 0
    const task: Promise<unknown> = Promise.resolve()
      .then(() => {
        if (
          pending.get(key) === task &&
          epoch === backendSessionGeneration.value &&
          backendSessionReady.value
        )
          return action()
      })
      .catch(async (error) => {
        if (pending.get(key) !== task) return
        started.delete(key)
        pending.delete(key)
        if (key === 'rules') await notifyRequestErrorForSession(error, {
          isCurrent: () => session.isCurrent() && attempt === rulesAttempt,
        })
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
      if (name === 'rules') ensure('rules', fetchRules)
    },
  )
  const visible = () => {
    if (document.visibilityState !== 'hidden' && backendSessionReady.value) {
      ensureSessionData()
    }
  }
  document.addEventListener('visibilitychange', visible)
  const stop = () => {
    rulesAttempt++
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
