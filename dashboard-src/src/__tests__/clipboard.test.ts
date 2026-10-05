import { afterEach, expect, it, vi } from 'vitest'
import { copyToClipboard } from '@/helper/clipboard'
import { showNotification } from '@/helper/notification'
import { notifyRequestError } from '@/helper/requestError'
vi.mock('@/helper/notification', () => ({ showNotification: vi.fn() }))
vi.mock('@/helper/requestError', () => ({ notifyRequestError: vi.fn() }))
afterEach(() => { vi.restoreAllMocks(); vi.clearAllMocks(); document.body.innerHTML = '' })
it('reports success only after the native clipboard acknowledges the write', async () => {
  vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue()
  await copyToClipboard('hello')
  expect(navigator.clipboard.writeText).toHaveBeenCalledWith('hello')
  expect(showNotification).toHaveBeenCalledWith(expect.objectContaining({ content: 'copySuccess' }))
  expect(notifyRequestError).not.toHaveBeenCalled()
})
it.each([false, 'throws', true])('checks the fallback result (%s), cleans up and restores focus', async (outcome) => {
  vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('not permitted'))
  const original = document.execCommand
  document.execCommand = vi.fn(() => { if (outcome === 'throws') throw new Error('denied'); return outcome === true })
  const input = document.createElement('input'); document.body.append(input); input.focus()
  try {
    await copyToClipboard('text that must not appear in an error')
    expect(document.querySelector('textarea')).toBeNull()
    expect(document.activeElement).toBe(input)
    expect(showNotification).toHaveBeenCalledTimes(outcome === true ? 1 : 0)
    expect(notifyRequestError).toHaveBeenCalledTimes(outcome === true ? 0 : 1)
  } finally { document.execCommand = original }
})
