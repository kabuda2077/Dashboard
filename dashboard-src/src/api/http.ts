// Axios transport boundary: capture the active connection and its epoch before dispatch.
// Router/page initialization must not be a static dependency of this module.
import { hasHostBridge } from '@/composables/hostBridge'
import { ROUTE_NAME } from '@/constant'
import {
  backendSessionGeneration,
  backendSessionReady,
  captureBackendSession,
  markBackendUnauthorized,
} from '@/helper/backendSession'
import { showNotification } from '@/helper/notification'
import { markRequestErrorHandled } from '@/helper/requestError'
import { getUrlFromBackend } from '@/helper/utils'
import { activeBackend, activeUuid } from '@/store/setup'
import axios, { AxiosError, CanceledError } from 'axios'
import { nextTick, watch } from 'vue'
import type { RouteLocationRaw } from 'vue-router'

declare module 'axios' {
  interface InternalAxiosRequestConfig {
    backendSession?: ReturnType<typeof captureBackendSession>
    releaseBackendRequest?: () => void
  }
}

const requests = new Set<AbortController>()
watch(
  [backendSessionGeneration, backendSessionReady],
  () => {
    for (const controller of requests) controller.abort()
    requests.clear()
  },
  { flush: 'sync' },
)

axios.interceptors.request.use(
  (config) => {
    if (activeBackend.value && config.url?.startsWith('/')) {
      config.backendSession = captureBackendSession()
      const controller = new AbortController()
      const originalSignal = config.signal
      const abort = () => controller.abort()
      if (originalSignal?.aborted) controller.abort()
      else originalSignal?.addEventListener?.('abort', abort, { once: true })
      config.signal = controller.signal
      requests.add(controller)
      config.releaseBackendRequest = () => {
        requests.delete(controller)
        originalSignal?.removeEventListener?.('abort', abort)
      }
      if (!config.timeout || !Number.isFinite(config.timeout) || config.timeout < 0) config.timeout = 30000
      config.baseURL = getUrlFromBackend(activeBackend.value)
      config.headers['Authorization'] = 'Bearer ' + activeBackend.value.password
    }
    return config
  },
  undefined,
  { synchronous: true },
)

let unauthorizedNotificationGeneration = -1
const navigateAfterAuthFailure = (target: RouteLocationRaw) => {
  const generation = backendSessionGeneration.value
  // Do not pull the router (and its page/assembly imports) into interceptor initialization.
  void import('@/router')
    .then(({ default: router }) => {
      if (generation === backendSessionGeneration.value) return router.push(target)
    })
    .catch(() => {})
}

axios.interceptors.response.use(
  (response) => {
    response.config.releaseBackendRequest?.()
    if (
      response.config.backendSession &&
      (!response.config.backendSession.isCurrent() || response.config.signal?.aborted)
    ) {
      throw new CanceledError('Backend session changed', response.config)
    }
    return response
  },
  async (
    error: AxiosError<{
      message: string
    }>,
  ) => {
    error.config?.releaseBackendRequest?.()
    if (axios.isCancel(error)) return Promise.reject(error)
    if (
      error.config?.backendSession &&
      (!error.config.backendSession.isCurrent() || error.config.signal?.aborted)
    ) {
      return Promise.reject(new CanceledError('Backend session changed', error.config))
    }
    if (error.status === 401 && error.config?.backendSession && activeUuid.value) {
      markRequestErrorHandled(error)
      markBackendUnauthorized()
      const generation = backendSessionGeneration.value
      if (unauthorizedNotificationGeneration !== generation) {
        unauthorizedNotificationGeneration = generation
        const currentBackendUuid = activeUuid.value
        if (hasHostBridge) {
          navigateAfterAuthFailure({ name: ROUTE_NAME.core, query: { connection: 'unauthorized' } })
        } else {
          activeUuid.value = null
          navigateAfterAuthFailure({
            name: ROUTE_NAME.setup,
            query: { editBackend: currentBackendUuid },
          })
        }
        nextTick(() => {
          if (hasHostBridge && !error.config?.backendSession?.isCurrent()) return
          showNotification({ content: 'unauthorizedTip' })
        })
      }
    }

    return Promise.reject(error)
  },
)
