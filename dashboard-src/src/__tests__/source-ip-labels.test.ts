import { sourceIPLabelList } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import { getIPLabelFromMap } from '@/helper/sourceip'
import { beforeEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return { sourceIPLabelList: ref([]) }
})
vi.mock('@/store/setup', async () => {
  const { ref } = await import('vue')
  return { activeBackend: ref({ uuid: 'local' }) }
})

beforeEach(async () => {
  sourceIPLabelList.value = []
  activeBackend.value = { uuid: 'local' } as typeof activeBackend.value
  await nextTick()
})

it('matches IPv6 labels and suffixes across interface zones while retaining raw unmatched addresses', async () => {
  sourceIPLabelList.value = [
    { id: 'laptop', key: 'fe80::1234%eth0', label: 'laptop' },
    { id: 'phone', key: '::abcd', label: 'phone' },
  ]
  await nextTick()

  expect(getIPLabelFromMap('fe80::1234%wlan0')).toBe('laptop')
  expect(getIPLabelFromMap('fe80::1234')).toBe('laptop')
  expect(getIPLabelFromMap('fe80::abcd%12')).toBe('phone')
  expect(getIPLabelFromMap('fe80::9999%eth0')).toBe('fe80::9999%eth0')
  expect(getIPLabelFromMap('')).toBe('Inner')
})

it('preserves raw-zone regex matching, CIDR matching and label cache invalidation', async () => {
  sourceIPLabelList.value = [
    { id: 'wireless', key: '/%wifi$', label: 'wireless' },
    { id: 'link-local', key: 'fe80::/10', label: 'link-local' },
  ]
  await nextTick()
  expect(getIPLabelFromMap('fe80::1%wifi')).toBe('wireless')
  expect(getIPLabelFromMap('fe80::1%eth0')).toBe('link-local')

  sourceIPLabelList.value = [{ id: 'renamed', key: 'fe80::1', label: 'renamed', scope: ['local'] }]
  await nextTick()
  expect(getIPLabelFromMap('fe80::1%eth0')).toBe('renamed')
  sourceIPLabelList.value[0].label = 'updated'
  await nextTick()
  expect(getIPLabelFromMap('fe80::1%eth0')).toBe('updated')
  activeBackend.value = { uuid: 'other' } as typeof activeBackend.value
  await nextTick()
  expect(getIPLabelFromMap('fe80::1%eth0')).toBe('fe80::1%eth0')
  activeBackend.value = { uuid: 'local' } as typeof activeBackend.value
  await nextTick()
  expect(getIPLabelFromMap('fe80::1%eth0')).toBe('updated')
})

it('retains IPv4 exact and CIDR labels and raw unmatched addresses', async () => {
  sourceIPLabelList.value = [
    { id: 'desktop', key: '192.0.2.10', label: 'desktop' },
    { id: 'local-network', key: '192.0.2.0/24', label: 'local-network' },
  ]
  await nextTick()

  expect(getIPLabelFromMap('192.0.2.10')).toBe('desktop')
  expect(getIPLabelFromMap('192.0.2.11')).toBe('local-network')
  expect(getIPLabelFromMap('198.51.100.1')).toBe('198.51.100.1')
})
