import { CONNECTIONS_TABLE_ACCESSOR_KEY, PROXY_CHAIN_DIRECTION } from '@/constant'
import {
  createGetConnectionDisplayValue,
  type ConnectionAccessor,
} from '@/assembly/connections/accessor'
import type { Connection } from '@/types'
import { describe, expect, it } from 'vitest'

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
  })
})
