import { showNotification } from '@/helper/notification'

export const showHostNotice = (message: string) => {
  if (!message) return
  const type = message.includes('失败') ? 'alert-error'
    : message.startsWith('正在') || message.includes('新版本') ? 'alert-info'
      : message.includes('管理员权限') || message.includes('UAC') ? 'alert-warning' : 'alert-success'
  showNotification({ content: message, key: `core-host-${message}`, type })
}
