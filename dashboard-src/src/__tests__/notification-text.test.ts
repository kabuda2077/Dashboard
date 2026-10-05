import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { showHostNotice } from '@/helper/hostNotice'
import { initNotification } from '@/helper/notification'
import { notifyRequestError } from '@/helper/requestError'

beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.clearAllTimers()
  vi.useRealTimers()
  document.body.replaceChildren()
})

it.each(['api', 'host'])('renders %s error messages as text through the real translation path', (source) => {
  const root = document.createElement('div')
  document.body.append(root)
  initNotification(ref(root))
  const payload = '<img src=x onerror="window.__notificationInjected=true">\nnext line'
  if (source === 'api') {
    notifyRequestError({ isAxiosError: true, response: { data: { message: payload } }, message: 'error' })
  } else {
    showHostNotice(payload)
  }
  expect(root.querySelector('img')).toBeNull()
  expect(root.querySelector('.app-toast__content')?.textContent).toBe(payload)
})
