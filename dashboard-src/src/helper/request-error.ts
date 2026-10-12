// 请求失败的提示工具。
//
// api 层不再全局拦截报错(见 api/http.ts):是否打扰用户由发起请求的业务层决定 ——
// 只有用户手动触发的动作(点按钮、提交表单)才 try-catch 后调用这里弹提示;
// 后台自动拉取(轮询、切后端后的初始化、反查 DNS 等)失败一律静默。
import { hasHostBridge } from '@/composables/hostBridge'
import { activeBackend } from '@/store/setup'
import axios from 'axios'
import { watch } from 'vue'
import { backendSessionGeneration, captureBackendSession } from './backendSession'
import { showNotification } from './notification'

const handledRequestErrors = new WeakSet<object>()

export const markRequestErrorHandled = (error: unknown) => {
  if ((typeof error === 'object' && error !== null) || typeof error === 'function') {
    handledRequestErrors.add(error)
  }
}

export const isRequestErrorHandled = (error: unknown) =>
  ((typeof error === 'object' && error !== null) || typeof error === 'function') &&
  handledRequestErrors.has(error)

export const getRequestErrorMessage = (error: unknown): string => {
  if (axios.isAxiosError<{ message?: string }>(error)) {
    return error.response?.data?.message || error.message
  }

  if (error instanceof Error) {
    return error.message
  }

  return String(error)
}

// 用 message 作为 key,同一个错误重复触发时复用同一条 toast,不会刷屏。
// 传 key 则顶掉那条(通常是 notifyActionPending 弹的「执行中」),免得两条并排。
export const notifyRequestError = (error: unknown, key?: string) => {
  if (axios.isCancel(error) || isRequestErrorHandled(error)) return
  const message = getRequestErrorMessage(error)
  const url = axios.isAxiosError(error) ? decodeURIComponent(error.config?.url || '') : ''

  showNotification({
    key: key || message,
    content: url ? `${url} \n${message}` : message,
    type: 'alert-error',
  })
}

export const notifyRequestErrorForSession = async (
  error: unknown,
  session: ReturnType<typeof captureBackendSession>,
  key?: string,
) => {
  if (!session.isCurrent() || axios.isCancel(error) || isRequestErrorHandled(error)) return
  if (
    axios.isAxiosError(error) &&
    error.config?.backendSession &&
    !error.config.backendSession.isCurrent()
  )
    return
  // Only diagnose manual browser requests bound to the active Clash-compatible API.
  // Core errors and external absolute URLs retain their existing reporting path.
  if (
    !hasHostBridge &&
    activeBackend.value &&
    axios.isAxiosError(error) &&
    error.config?.backendSession?.isCurrent() &&
    error.config.url?.startsWith('/') &&
    !error.config.url.startsWith('//') &&
    !error.response
  ) {
    const backend = { ...activeBackend.value }
    const controller = new AbortController()
    const stopWatching = watch(backendSessionGeneration, () => controller.abort(), {
      flush: 'sync',
    })
    try {
      const { describeConnectionError, getBackendProbeUrl, isOpaqueNetworkError } =
        await import('./connectivity')
      if (!session.isCurrent() || controller.signal.aborted) return
      const message = getRequestErrorMessage(error)
      if (isOpaqueNetworkError(message)) {
        const content = await describeConnectionError(
          message,
          getBackendProbeUrl(backend),
          controller.signal,
        )
        if (session.isCurrent() && !controller.signal.aborted)
          showNotification({
            key: key || message,
            content,
            type: 'alert-error',
          })
        return
      }
    } catch {
      if (!session.isCurrent() || controller.signal.aborted) return
    } finally {
      stopWatching()
    }
  }
  if (session.isCurrent()) notifyRequestError(error, key)
}

// For click handlers that do not need to return a result. Capture before starting
// the request so a later backend switch cannot turn an obsolete error into a toast.
export const runManualRequest = async (action: () => Promise<unknown>) => {
  const session = captureBackendSession()
  try {
    await action()
  } catch (error) {
    await notifyRequestErrorForSession(error, session)
  }
}
