import { showNotification } from './notification'
import { notifyRequestError } from './requestError'

export const copyText = async (text: string) => {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return
    }
  } catch { /* Try the browser's legacy copy mechanism, but check its result. */ }
  const previous = document.activeElement
  const area = document.createElement('textarea')
  area.value = text
  area.style.cssText = 'position:fixed;left:-10000px;top:0'
  document.body.append(area)
  try {
    area.select()
    if (!document.execCommand('copy')) throw new Error('Clipboard access was denied.')
  } finally {
    area.remove()
    if (previous instanceof HTMLElement) previous.focus({ preventScroll: true })
  }
}

export const copyToClipboard = async (text: string) => {
  try {
    await copyText(text)
    showNotification({ content: 'copySuccess', type: 'alert-success', timeout: 2000 })
  } catch (error) {
    notifyRequestError(error)
  }
}
