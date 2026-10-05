import { showNotification } from '@/helper/notification'
import { i18n } from '@/i18n'

export const showHostNotice = (
  message: string,
  severity: 'info' | 'warning' | 'error' | 'success' = 'info',
) => {
  if (!message) return
  const key = `desktop.result.${message}`
  const content = i18n.global.te(key) ? i18n.global.t(key) : message
  showNotification({ content, key: `core-host-${message}`, type: `alert-${severity}` })
}
