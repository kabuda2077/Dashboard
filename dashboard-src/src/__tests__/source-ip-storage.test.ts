import { afterEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

vi.mock('@/helper/notification', () => ({ showNotification: vi.fn() }))

afterEach(() => localStorage.clear())

it('loads a saved mixed label list without clearing it and persists a correction of the bad regex', async () => {
  const key = 'config/source-ip-label-list'
  const labels = [
    { id: 'valid', key: '192.0.2.10', label: 'desktop' },
    { id: 'broken', key: '/[', label: 'other network' },
  ]
  const saved = JSON.stringify(labels)
  localStorage.setItem(key, saved)

  // Load the real store only after seeding storage: mocked refs cannot detect
  // the storage composable replacing the entire list with its default value.
  const { sourceIPLabelList } = await import('@/store/settings')
  await nextTick()
  expect(sourceIPLabelList.value).toEqual(labels)
  expect(localStorage.getItem(key)).toBe(saved)

  const { getIPLabelFromMap } = await import('@/helper/source-ip')
  expect(getIPLabelFromMap('192.0.2.10')).toBe('desktop')
  expect(getIPLabelFromMap('198.51.100.1')).toBe('198.51.100.1')
  await nextTick()
  expect(localStorage.getItem(key)).toBe(saved)

  sourceIPLabelList.value[1].key = '/^198[.]51[.]100[.]'
  await nextTick()
  expect(getIPLabelFromMap('192.0.2.10')).toBe('desktop')
  expect(getIPLabelFromMap('198.51.100.1')).toBe('other network')
  expect(JSON.parse(localStorage.getItem(key)!)).toEqual([
    labels[0],
    { ...labels[1], key: '/^198[.]51[.]100[.]' },
  ])
})
