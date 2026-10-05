import type { HostMessage } from '@/composables/hostBridge'

export const getAppUpdateFeedback = (message: HostMessage): string | undefined => {
  if (message.type !== 'appUpdateResult' || !message.manual || typeof message.result !== 'string')
    return
  switch (message.result) {
    case 'available':
      return message.latestVersion ? `发现 v${message.latestVersion}` : '发现新版本'
    case 'upToDate':
      return message.currentVersion ? `已是最新版本 v${message.currentVersion}` : '已是最新版本'
    case 'failed':
      return '检查失败，请稍后重试'
    case 'busy':
      return '正在检查，请稍候'
  }
}
