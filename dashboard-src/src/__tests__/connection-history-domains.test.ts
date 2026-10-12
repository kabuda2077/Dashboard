import type { Connection } from '@/types'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ref } from 'vue'

vi.mock('@/store/setup', () => ({ activeBackend: ref({ uuid: 'domain-tests' }) }))
vi.mock('@/store/settings', () => ({ customBackgroundURL: ref('') }))
vi.mock('@/helper', () => ({
  getConnectionHostname: (connection: Connection) => connection.metadata.host,
  getConnectionDownload: (connection: Connection) => connection.download,
  getConnectionUpload: (connection: Connection) => connection.upload,
  getConnectionChains: () => ['DIRECT'],
  getConnectionSourceIP: () => '192.0.2.1',
  getProcessFromConnection: () => 'test.exe',
}))
beforeEach(() => { vi.resetModules(); vi.useFakeTimers() })
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers() })

it('aggregates subdomains without merging unrelated public or private suffix domains', async () => {
  const { aggregateConnections } = await import('@/store/conn-history')
  const { ConnectionHistoryType } = await import('@/helper/indexeddb')
  const hosts = ['www.bbc.co.uk', 'news.bbc.co.uk', 'www.amazon.co.uk', 'a.github.io', 'b.github.io']
  const connections = hosts.map((host, index) => ({
    id: String(index), metadata: { host }, download: 10, upload: 1,
  }) as Connection)
  expect(aggregateConnections(connections, ConnectionHistoryType.Destination)).toEqual([
    { key: 'bbc.co.uk', download: 20, upload: 2, count: 2 },
    { key: 'amazon.co.uk', download: 10, upload: 1, count: 1 },
    { key: 'a.github.io', download: 10, upload: 1, count: 1 },
    { key: 'b.github.io', download: 10, upload: 1, count: 1 },
  ])
})

it('preserves IP addresses and single-label hosts', async () => {
  const { aggregateConnections } = await import('@/store/conn-history')
  const { ConnectionHistoryType } = await import('@/helper/indexeddb')
  for (const host of ['192.0.2.1', '2001:db8::1', 'localhost', '']) {
    const connection = { id: host, metadata: { host }, download: 1, upload: 0 } as Connection
    expect(aggregateConnections([connection], ConnectionHistoryType.Destination)[0]?.key).toBe(host)
  }
})
