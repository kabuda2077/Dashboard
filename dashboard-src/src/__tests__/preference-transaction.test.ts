import { afterEach, beforeEach, expect, it, vi } from 'vitest'
beforeEach(() => {
  vi.resetModules()
  localStorage.clear()
})
afterEach(() => vi.restoreAllMocks())
it('rejects a malformed current document before changing any preference', async () => {
  const { parseSettingsDocument } = await import('@/helper/utils')
  localStorage.setItem('config/a', 'old')
  expect(() =>
    parseSettingsDocument({
      schemaVersion: 2,
      preferences: { 'config/a': 'new', 'config/b': false },
    }),
  ).toThrow()
  expect(localStorage.getItem('config/a')).toBe('old')
})
it('rolls back a partial local import when storage rejects a later key', async () => {
  const { applyDashboardSettingsToStorage } = await import('@/helper/utils')
  localStorage.setItem('config/a', 'old')
  const original = localStorage.setItem.bind(localStorage)
  vi.spyOn(localStorage, 'setItem').mockImplementation((key, value) => {
    if (key === 'config/b' && value === 'new') throw new DOMException('quota', 'QuotaExceededError')
    original(key, value)
  })
  expect(() =>
    applyDashboardSettingsToStorage({ 'config/a': 'changed', 'config/b': 'new' }),
  ).toThrow()
  expect(localStorage.getItem('config/a')).toBe('old')
  expect(localStorage.getItem('config/b')).toBeNull()
})
it('blocks durable persistence if local rollback itself fails', async () => {
  const { applyDashboardSettingsToStorage } = await import('@/helper/utils')
  const { canPersistPreferences } = await import('@/helper/settingsChanges')
  localStorage.setItem('config/a', 'old')
  vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
    throw new Error('storage unavailable')
  })
  expect(() => applyDashboardSettingsToStorage({ 'config/a': 'changed' })).toThrow('roll back')
  expect(canPersistPreferences()).toBe(false)
})
