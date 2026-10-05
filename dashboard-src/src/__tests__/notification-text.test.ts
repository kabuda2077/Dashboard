import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ref } from 'vue'

vi.mock('@/i18n', () => ({ i18n: { global: { t: (value: string) => value } } }))
beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
})
afterEach(() => {
  vi.useRealTimers()
  document.body.replaceChildren()
})
it('renders API and host messages as text, never as privileged HTML', async () => {
  const root = document.createElement('div')
  document.body.append(root)
  const { initNotification, showNotification } = await import('@/helper/notification')
  initNotification(ref(root))
  const payload =
    '<img src=x onerror="window.chrome.webview.postMessage({type: `stop`})">\nnext line'
  showNotification({ content: payload, type: 'alert-error' })
  expect(root.querySelector('img')).toBeNull()
  expect(root.querySelector('.app-toast__content')?.textContent).toBe(payload)
  expect(root.querySelector('[role="alert"]')).not.toBeNull()
})
