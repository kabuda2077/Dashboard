import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

vi.mock('@/helper', () => ({
  getConnectionChains: (connection: { chains: string[] }) => connection.chains,
  getConnectionDownload: (connection: { download: number }) => connection.download,
  getConnectionHostname: () => '',
  getConnectionSourceIP: () => '',
  getConnectionUpload: (connection: { upload: number }) => connection.upload,
  getProcessFromConnection: () => '',
}))

vi.mock('@/store/setup', () => ({
  activeBackend: ref({ uuid: 'test-backend' }),
}))

describe('connection history aggregation', () => {
  it('aggregates proxy-group traffic by the last chain entry', async () => {
    const { ConnectionHistoryType } = await import('@/helper/indexeddb')
    const { aggregateConnections } = await import('@/store/connHistory')
    const connections = [
      { chains: ['Node A', 'Group A'], download: 100, upload: 20 },
      { chains: ['Node B', 'Group A'], download: 50, upload: 10 },
      { chains: ['DIRECT'], download: 30, upload: 5 },
    ]

    expect(aggregateConnections(connections as never[], ConnectionHistoryType.ProxyGroup)).toEqual([
      { key: 'Group A', download: 150, upload: 30, count: 2 },
      { key: 'DIRECT', download: 30, upload: 5, count: 1 },
    ])
  })
})
