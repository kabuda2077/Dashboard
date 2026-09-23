// api 层 · axios 实例的全局拦截器。
// 这是 api 层唯一允许依赖 store/setup 的地方:请求需要从 activeBackend 取得
// 当前连接目标(baseURL / 鉴权)。其余 api 文件不得依赖上层。
import { ROUTE_NAME } from '@/constant'
import { hasHostBridge } from '@/composables/hostBridge'
import { backendSessionGeneration, captureBackendSession } from '@/helper/backendSession'
import { showNotification } from '@/helper/notification'
import { markRequestErrorHandled } from '@/helper/requestError'
import { getUrlFromBackend } from '@/helper/utils'
import router from '@/router'
import { activeBackend, activeUuid } from '@/store/setup'
import axios, { AxiosError, CanceledError } from 'axios'

declare module 'axios' {
  interface InternalAxiosRequestConfig {
    backendSession?: ReturnType<typeof captureBackendSession>
  }
}
import { nextTick } from 'vue'

axios.interceptors.request.use((config) => {
  if (activeBackend.value && config.url?.startsWith('/')) {
    config.backendSession = captureBackendSession()
    config.baseURL = getUrlFromBackend(activeBackend.value)
    config.headers['Authorization'] = 'Bearer ' + activeBackend.value.password
  }
  return config
}, undefined, { synchronous: true })

let unauthorizedNotificationGeneration = -1

axios.interceptors.response.use(
  (response) => {
    if (response.config.backendSession && !response.config.backendSession.isCurrent()) {
      throw new CanceledError('Backend session changed', response.config)
    }
    return response
  },
  async (
    error: AxiosError<{
      message: string
    }>,
  ) => {
    if (axios.isCancel(error)) return Promise.reject(error)
    if (error.config?.backendSession && !error.config.backendSession.isCurrent()) {
      return Promise.reject(new CanceledError('Backend session changed', error.config))
    }
    if (error.status === 401 && error.config?.backendSession && activeUuid.value) {
      markRequestErrorHandled(error)
      const generation = backendSessionGeneration.value
      if (unauthorizedNotificationGeneration !== generation) {
        unauthorizedNotificationGeneration = generation
        const currentBackendUuid = activeUuid.value
        if (hasHostBridge) {
          void router.push({ name: ROUTE_NAME.core, query: { connection: 'unauthorized' } })
        } else {
          activeUuid.value = null
          void router.push({
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
