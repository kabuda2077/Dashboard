import { i18n } from '@/i18n'
import { type Ref } from 'vue'

const t = i18n.global.t

type NotificationType = 'alert-warning' | 'alert-success' | 'alert-error' | 'alert-info' | ''
type NotificationKind = 'warning' | 'success' | 'error' | 'info' | 'neutral'

const NOTIFICATION_ICONS: Record<NotificationKind, string> = {
  success: `
    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
      <path stroke-linecap="round" stroke-linejoin="round" d="m4.5 12.75 6 6 9-13.5" />
    </svg>
  `,
  error: `
    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
      <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
    </svg>
  `,
  warning: `
    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
      <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9.303 3.376c.866 1.5-.217 3.374-1.948 3.374H4.645c-1.73 0-2.813-1.874-1.948-3.374L10.052 3.37c.866-1.5 3.03-1.5 3.896 0l7.355 12.756ZM12 15.75h.008v.008H12v-.008Z" />
    </svg>
  `,
  info: `
    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
      <path stroke-linecap="round" stroke-linejoin="round" d="m11.25 11.25.041-.02a.75.75 0 0 1 1.063.852l-.708 2.836a.75.75 0 0 0 1.063.853l.041-.021M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9-3.75h.008v.008H12V8.25Z" />
    </svg>
  `,
  neutral: `
    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
      <path stroke-linecap="round" stroke-linejoin="round" d="M11.25 4.533A9.707 9.707 0 0 0 6 3c-1.846 0-3.543.507-5 1.395v15.36A9.697 9.697 0 0 1 6 18c1.956 0 3.77.578 5.25 1.566m0-15.033A9.707 9.707 0 0 1 16.5 3c1.846 0 3.543.507 5 1.395v15.36A9.697 9.697 0 0 0 16.5 18a9.707 9.707 0 0 0-5.25 1.566m0-15.033v15.033" />
    </svg>
  `,
}

const getNotificationKind = (type: NotificationType): NotificationKind => {
  return type ? (type.replace('alert-', '') as NotificationKind) : 'neutral'
}

const alertMap = new Map<
  string,
  {
    timer: number
    alert: HTMLElement
    progressBar: HTMLElement
    startTime: number
    remainingTime: number
    isPaused: boolean
  }
>()
let toastRef: Ref<HTMLElement> | null = null

export const initNotification = (toast: Ref<HTMLElement>) => {
  toastRef = toast
}

const pauseTimer = (alertKey: string) => {
  const alertData = alertMap.get(alertKey)
  if (alertData && alertData.timer !== -1 && !alertData.isPaused) {
    clearTimeout(alertData.timer)
    alertData.isPaused = true
    alertData.remainingTime = alertData.remainingTime - (Date.now() - alertData.startTime)
    alertData.progressBar.style.animationPlayState = 'paused'
  }
}

const resumeTimer = (alertKey: string) => {
  const alertData = alertMap.get(alertKey)
  if (alertData && alertData.timer !== -1 && alertData.isPaused) {
    alertData.isPaused = false
    alertData.startTime = Date.now()
    alertData.timer = setTimeout(() => {
      alertMap.delete(alertKey)
      removeAlert(alertData.alert)
    }, alertData.remainingTime)
    alertData.progressBar.style.animationPlayState = 'running'
  }
}

const setTimer = (
  alert: HTMLElement,
  timeout: number,
  alertKey?: string,
  progressBar?: HTMLElement | null,
) => {
  let timer = -1

  progressBar?.parentElement?.toggleAttribute('hidden', timeout === 0)

  if (timeout !== 0) {
    if (progressBar) {
      progressBar.style.animation = 'none'
      // 读取布局以确保复用同一条提示时，倒计时动画能够从头开始。
      void progressBar.offsetWidth
      progressBar.style.animation = `progressBar ${timeout}ms linear forwards`
    }

    timer = setTimeout(() => {
      if (alertKey) {
        alertMap.delete(alertKey)
      }
      removeAlert(alert)
    }, timeout)
  }

  if (alertKey && progressBar) {
    alertMap.set(alertKey, {
      alert,
      timer,
      progressBar,
      startTime: Date.now(),
      remainingTime: timeout,
      isPaused: false,
    })
  }
}

const removeAlert = (alert: HTMLElement) => {
  if (alert.classList.contains('is-leaving')) return

  alert.classList.add('is-leaving')
  window.setTimeout(() => alert.remove(), 160)
}

const closeAlert = (alert: HTMLElement, alertKey?: string) => {
  if (alertKey) {
    const alertData = alertMap.get(alertKey)
    if (alertData) {
      clearTimeout(alertData.timer)
      alertMap.delete(alertKey)
    }
  }
  removeAlert(alert)
}

const setAlert = (
  alert: HTMLElement,
  content: string,
  params: Record<string, string>,
  type: NotificationType,
  alertKey: string,
): HTMLElement | null => {
  const kind = getNotificationKind(type)

  alert.className = 'app-toast'
  alert.dataset.toastType = kind
  alert.setAttribute('role', kind === 'error' ? 'alert' : 'status')
  alert.setAttribute('aria-live', kind === 'error' ? 'assertive' : 'polite')

  const accent = document.createElement('div')
  accent.className = 'app-toast__accent'
  accent.setAttribute('aria-hidden', 'true')

  const icon = document.createElement('div')
  icon.className = 'app-toast__icon'
  icon.setAttribute('aria-hidden', 'true')
  icon.innerHTML = NOTIFICATION_ICONS[kind]

  const contentDiv = document.createElement('div')
  contentDiv.className = 'app-toast__content'
  contentDiv.innerHTML = t(content, params)

  const closeButton = document.createElement('button')
  closeButton.type = 'button'
  closeButton.className = 'app-toast__close btn btn-circle btn-ghost btn-xs'
  closeButton.setAttribute('aria-label', t('close'))
  closeButton.innerHTML = `
    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.75" stroke="currentColor" aria-hidden="true">
      <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
    </svg>
  `
  closeButton.addEventListener('click', () => closeAlert(alert, alertKey))

  const progressContainer = document.createElement('div')
  progressContainer.className = 'app-toast__progress-track'

  const progressBar = document.createElement('div')
  progressBar.className = 'app-toast__progress'

  progressContainer.appendChild(progressBar)

  alert.replaceChildren(accent, icon, contentDiv, closeButton, progressContainer)

  alert.onmouseenter = () => pauseTimer(alertKey)
  alert.onmouseleave = () => resumeTimer(alertKey)

  return progressBar
}

/** 收掉一条还挂着的提示 —— 动作结束却没有自己的结果提示时用(如已被内部提示接手)。 */
export const dismissNotification = (key: string) => {
  const alertData = alertMap.get(key)

  if (!alertData) return

  closeAlert(alertData.alert, key)
}

/**
 * 用户点下动作时立刻弹一条「执行中」。
 *
 * 按钮转圈是唯一反馈时,清缓存这类几十毫秒就回来的动作只会让按钮闪一下 ——
 * 看不出到底点没点上。这条提示不自动消失(timeout 0),等成功或失败的提示
 * 用同一个 key 顶掉它,一次动作从头到尾只占一条 toast。
 *
 * @param label 动作名的 i18n key
 * @returns 后续成功/失败提示要复用的 key
 */
export const notifyActionPending = (label: string) => {
  const key = `action:${label}`

  showNotification({
    key,
    content: 'actionRunning',
    params: { action: t(label) },
    type: 'alert-info',
    timeout: 0,
  })

  return key
}

export const showNotification = ({
  content,
  params = {},
  key,
  type = 'alert-warning',
  timeout = 3000,
}: {
  content: string
  params?: Record<string, string>
  key?: string
  type?: NotificationType
  timeout?: number
}) => {
  const alertKey = key || content

  if (alertKey && alertMap.has(alertKey)) {
    const { alert, timer } = alertMap.get(alertKey)!
    clearTimeout(timer)

    const progressBar = setAlert(alert, content, params, type, alertKey)
    setTimer(alert, timeout, alertKey, progressBar)
    return
  }

  const alert = document.createElement('div')

  const progressBar = setAlert(alert, content, params, type, alertKey)
  toastRef?.value?.insertBefore(alert, toastRef?.value?.firstChild)
  setTimer(alert, timeout, alertKey, progressBar)
}
