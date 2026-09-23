import type { HostMessage } from '@/composables/hostBridge'

type AppUpdateResult = 'available' | 'upToDate' | 'failed' | 'busy'

export type AppUpdateResultMessage = HostMessage & {
  result?: AppUpdateResult
  manual?: boolean
  currentVersion?: string
  latestVersion?: string
}

const withVersion = (prefix: string, version?: string) =>
  version ? `${prefix} v${version}` : prefix

export const getAppUpdateFeedback = (message: AppUpdateResultMessage): string | undefined => {
  if (message.type !== 'appUpdateResult' || !message.manual) return

  const result = message

  switch (result.result) {
    case 'available':
      return withVersion('发现', result.latestVersion)
    case 'upToDate':
      return withVersion('已是最新版本', result.currentVersion)
    case 'failed':
      return '检查失败，请稍后重试'
    case 'busy':
      return '正在检查，请稍候'
  }
}
