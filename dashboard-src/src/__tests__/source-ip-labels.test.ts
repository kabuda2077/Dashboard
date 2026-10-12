import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import { isResolvableIP, reverseName } from '@/helper/reverse-dns'
import { getIPLabelFromMap } from '@/helper/source-ip'
import { resolveClientHostname, sourceIPLabelList } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

const dns = vi.hoisted(() => ({ query: vi.fn() }))
vi.mock('@/assembly/config', () => ({ queryDNS: dns.query }))
vi.mock('@/helper/backendSession', async () => {
  const { ref } = await import('vue')
  const generation = ref(0)
  return {
    backendSessionGeneration: generation,
    backendSessionReady: ref(true),
    captureBackendSession: () => {
      const current = generation.value
      return { isCurrent: () => current === generation.value }
    },
  }
})
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return { sourceIPLabelList: ref([]), resolveClientHostname: ref(false) }
})
vi.mock('@/store/setup', async () => {
  const { ref } = await import('vue')
  return { activeBackend: ref({ uuid: 'local' }) }
})

beforeEach(async () => {
  vi.useRealTimers()
  dns.query.mockReset().mockResolvedValue({ Answer: [] })
  localStorage.clear()
  resolveClientHostname.value = false
  ;(backendSessionGeneration as { value: number }).value++
  ;(backendSessionReady as { value: boolean }).value = true
  sourceIPLabelList.value = []
  activeBackend.value = { uuid: 'local' } as typeof activeBackend.value
  await nextTick()
})

afterEach(() => {
  resolveClientHostname.value = false
  vi.useRealTimers()
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

const flushDNS = async () => {
  for (let i = 0; i < 8; i++) await Promise.resolve()
  await nextTick()
}

it('only queries enabled ready sessions and keeps manual labels ahead of PTR names', async () => {
  expect(getIPLabelFromMap('192.0.2.10')).toBe('192.0.2.10')
  await flushDNS()
  expect(dns.query).not.toHaveBeenCalled()
  resolveClientHostname.value = true
  sourceIPLabelList.value = [{ id: 'manual', key: '192.0.2.10', label: 'desktop' }]
  await nextTick()
  expect(getIPLabelFromMap('192.0.2.10')).toBe('desktop')
  ;(backendSessionReady as { value: boolean }).value = false
  expect(getIPLabelFromMap('192.0.2.11')).toBe('192.0.2.11')
  await flushDNS()
  expect(dns.query).not.toHaveBeenCalled()
})

it('forms IPv4, mapped IPv4 and zone-free IPv6 PTR names without querying invalid or loopback IPs', () => {
  expect(reverseName('192.0.2.10')).toBe('10.2.0.192.in-addr.arpa')
  expect(reverseName('::ffff:192.0.2.10')).toBe('10.2.0.192.in-addr.arpa')
  expect(reverseName('fe80::1%eth0')).toBe(
    `${'100000000000000000000000000008ef'.split('').join('.')}.ip6.arpa`,
  )
  for (const ip of ['', 'bad', '127.0.0.1', '::1', '0.0.0.0', '::'])
    expect(isResolvableIP(ip)).toBe(false)
  expect(isResolvableIP('fe80::1%eth0')).toBe(true)
})

it('deduplicates lookups, displays cached names and expires positive and negative answers', async () => {
  vi.useFakeTimers()
  resolveClientHostname.value = true
  dns.query.mockResolvedValueOnce({ Answer: [{ type: 12, data: ' laptop.example. ' }] })
  expect(getIPLabelFromMap('192.0.2.10')).toBe('192.0.2.10')
  expect(getIPLabelFromMap('192.0.2.10')).toBe('192.0.2.10')
  await flushDNS()
  expect(dns.query).toHaveBeenCalledTimes(1)
  expect(dns.query.mock.calls[0][0]).toEqual({ name: '10.2.0.192.in-addr.arpa', type: 'PTR' })
  expect(getIPLabelFromMap('192.0.2.10')).toBe('laptop.example')
  getIPLabelFromMap('192.0.2.11')
  await flushDNS()
  expect(getIPLabelFromMap('192.0.2.11')).toBe('192.0.2.11')
  expect(dns.query).toHaveBeenCalledTimes(2)
  vi.setSystemTime(Date.now() + 11 * 60 * 1000)
  getIPLabelFromMap('192.0.2.11')
  await flushDNS()
  expect(dns.query).toHaveBeenCalledTimes(3)
  vi.setSystemTime(Date.now() + 46 * 60 * 1000)
  expect(getIPLabelFromMap('192.0.2.10')).toBe('192.0.2.10')
  await flushDNS()
  expect(dns.query).toHaveBeenCalledTimes(4)
})

it('aborts old requests and rejects late results after session changes or disabling resolution', async () => {
  let complete!: (value: { Answer: { type: number; data: string }[] }) => void
  dns.query.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        complete = resolve
      }),
  )
  resolveClientHostname.value = true
  getIPLabelFromMap('192.0.2.10')
  await flushDNS()
  const signal = dns.query.mock.calls[0][1] as AbortSignal
  ;(backendSessionGeneration as { value: number }).value++
  expect(signal.aborted).toBe(true)
  complete({ Answer: [{ type: 12, data: 'stale.example.' }] })
  await flushDNS()
  expect(localStorage.getItem('cache/reverse-dns-hostnames')).toBeNull()
  expect(getIPLabelFromMap('192.0.2.10')).toBe('192.0.2.10')
  resolveClientHostname.value = false
  await flushDNS()
  expect(localStorage.getItem('cache/reverse-dns-hostnames')).toBeNull()
})

it('falls back quietly on unsupported cores and retries only after a new session', async () => {
  resolveClientHostname.value = true
  dns.query.mockRejectedValueOnce({ response: { status: 404 } })
  getIPLabelFromMap('192.0.2.10')
  await flushDNS()
  expect(getIPLabelFromMap('192.0.2.11')).toBe('192.0.2.11')
  await flushDNS()
  expect(dns.query).toHaveBeenCalledTimes(1)
  ;(backendSessionGeneration as { value: number }).value++
  getIPLabelFromMap('192.0.2.11')
  await flushDNS()
  expect(dns.query).toHaveBeenCalledTimes(2)
})

it('bounds concurrent lookups and persistent entries, and separates edited endpoints without storing secrets', async () => {
  resolveClientHostname.value = true
  dns.query.mockResolvedValue({ Answer: [{ type: 12, data: 'client.example.' }] })
  for (let i = 1; i <= 20; i++) getIPLabelFromMap(`192.0.2.${i}`)
  await flushDNS()
  expect(dns.query).toHaveBeenCalledTimes(8)
  for (let i = 1; i <= 210; i++) {
    getIPLabelFromMap(`198.51.100.${i}`)
    await flushDNS()
  }
  const persisted = localStorage.getItem('cache/reverse-dns-hostnames')!
  expect(Object.keys(JSON.parse(persisted))).toHaveLength(200)
  expect(getIPLabelFromMap('198.51.100.100')).toBe('client.example')
  activeBackend.value = {
    uuid: 'local',
    host: 'other.example',
    password: 'secret-must-stay-out',
  } as typeof activeBackend.value
  expect(getIPLabelFromMap('198.51.100.100')).toBe('198.51.100.100')
  await flushDNS()
  expect(localStorage.getItem('cache/reverse-dns-hostnames')).not.toContain('secret-must-stay-out')
})

it('settles over-capacity reactive scans without continuously retrying live negative entries', async () => {
  vi.useFakeTimers()
  resolveClientHostname.value = true
  for (let pass = 0; pass < 40; pass++) {
    for (let i = 1; i <= 201; i++) getIPLabelFromMap(`192.0.2.${i}`)
    await flushDNS()
  }
  expect(dns.query).toHaveBeenCalledTimes(200)
  expect(localStorage.getItem('cache/reverse-dns-hostnames')).toBeNull()
  vi.setSystemTime(Date.now() + 11 * 60 * 1000)
  getIPLabelFromMap('192.0.2.201')
  await flushDNS()
  expect(dns.query).toHaveBeenCalledTimes(201)
})
