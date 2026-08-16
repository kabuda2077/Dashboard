import { CONNECTIONS_TABLE_ACCESSOR_KEY, PROXY_CHAIN_DIRECTION } from '@/constant'
import {
  createGetConnectionDisplayValue,
  createGetConnectionVisibleSearchValues,
  type ConnectionAccessor,
} from '@/assembly/connections/accessor'
import { connectionAccessor as clashConnectionAccessor } from '@/assembly/connections/clash'
import { connectionTableColumns } from '@/store/settings'
import type { Connection } from '@/types'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@/api/geoip', () => ({
  getGeoIPInfoSync: vi.fn(() => ({
    ip: '203.0.113.1',
    country: 'Testland',
    region: '',
    city: '',
    asn: '64500',
    organization: 'Example ASN',
  })),
}))

const accessor: ConnectionAccessor = {
  chains: () => ['ProxyA', 'ProxyB', 'DIRECT'],
  download: () => 2048,
  upload: () => 1024,
  start: () => Date.now(),
  rule: () => 'Rule',
  rulePayload: () => 'payload',
  sourceIP: () => '192.0.2.10',
  sourcePort: () => '55123',
  network: () => 'tcp',
  networkType: () => 'HTTP | tcp',
  hostname: () => 'example.test',
  host: () => 'example.test:443',
  process: () => 'browser.exe',
  destination: () => '203.0.113.1',
  inboundUser: () => '-',
  sniffHost: () => '',
  remoteAddress: () => '',
  protocol: () => '',
  outboundType: () => '',
  fromOutbound: () => '',
  smartBlock: () => undefined,
}

const connection = {
  downloadSpeed: 512,
  uploadSpeed: 128,
} as Connection

describe('connection field accessors', () => {
  it('derives display values through a backend accessor contract', () => {
    const getValue = createGetConnectionDisplayValue(accessor)

    expect(
      getValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.Chains, {
        mode: 'table',
        showFullProxyChain: false,
        proxyChainDirection: PROXY_CHAIN_DIRECTION.NORMAL,
      }),
    ).toBe('DIRECT → ProxyA')
    expect(getValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.Host, {
      mode: 'table',
      showFullProxyChain: true,
      proxyChainDirection: PROXY_CHAIN_DIRECTION.REVERSE,
    })).toBe('example.test:443')
    expect(getValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.Protocol, {
      mode: 'table',
      showFullProxyChain: true,
      proxyChainDirection: PROXY_CHAIN_DIRECTION.REVERSE,
    })).toBe('-')
    expect(getValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.GeoIP, {
      mode: 'table',
      showFullProxyChain: true,
      proxyChainDirection: PROXY_CHAIN_DIRECTION.REVERSE,
    })).toBe('Testland / Example ASN')
  })

  it('keeps GeoIP out of the default connection table columns', () => {
    expect(connectionTableColumns.value).not.toContain(CONNECTIONS_TABLE_ACCESSOR_KEY.GeoIP)
  })

  it('handles a missing process path', () => {
    const connectionWithoutProcess = {
      metadata: { process: '', processPath: undefined },
    } as unknown as Connection

    expect(clashConnectionAccessor.process(connectionWithoutProcess)).toBe('-')
  })

  it('excludes close actions from visible search values', () => {
    const getSearchValues = createGetConnectionVisibleSearchValues(accessor)
    const options = {
      mode: 'table' as const,
      showFullProxyChain: true,
      proxyChainDirection: PROXY_CHAIN_DIRECTION.NORMAL,
    }

    expect(
      getSearchValues(
        connection,
        [CONNECTIONS_TABLE_ACCESSOR_KEY.Host, CONNECTIONS_TABLE_ACCESSOR_KEY.Close],
        options,
      ),
    ).toEqual(['example.test:443'])
    expect(
      getSearchValues(connection, [CONNECTIONS_TABLE_ACCESSOR_KEY.Process], options),
    ).toEqual(['browser.exe'])
  })
})
