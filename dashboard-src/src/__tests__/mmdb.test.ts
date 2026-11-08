import { AsyncMMDBReader, type MMDBByteSource } from '@/helper/mmdb'
import { describe, expect, it, vi } from 'vitest'

// One search-tree node maps both halves of the IPv4 space to the same record.
const ipv4Database = (): MMDBByteSource => {
  const encoder = new TextEncoder()
  const encode = (value: string | number | Record<string, unknown>): number[] => {
    if (typeof value === 'string') {
      const bytes = encoder.encode(value)
      return [0x40 + bytes.length, ...bytes]
    }
    if (typeof value === 'number') return [0xa1, value]
    const entries = Object.entries(value)
    return [
      0xe0 + entries.length,
      ...entries.flatMap(([key, entry]) => [
        ...encode(key),
        ...encode(entry as Parameters<typeof encode>[0]),
      ]),
    ]
  }
  const bytes = new Uint8Array([
    0,
    0,
    17,
    0,
    0,
    17,
    ...new Array<number>(16).fill(0),
    ...encode({ country: { iso_code: 'US' } }),
    0xab,
    0xcd,
    0xef,
    ...encoder.encode('MaxMind.com'),
    ...encode({ node_count: 1, record_size: 24, ip_version: 4 }),
  ])
  return {
    size: bytes.length,
    read: vi.fn(async (offset: number, length: number) => bytes.slice(offset, offset + length)),
  }
}

describe('IPv4 MMDB address families', () => {
  it('looks up IPv4 and IPv4-mapped IPv6 addresses in the same tree', async () => {
    const reader = await AsyncMMDBReader.open(ipv4Database())
    expect(await reader.get('1.2.3.4')).toEqual({ country: { iso_code: 'US' } })
    expect(await reader.get('::ffff:1.2.3.4')).toEqual(await reader.get('1.2.3.4'))
  })

  it('returns no record for IPv6 without reading the IPv4 tree', async () => {
    const source = ipv4Database()
    const reader = await AsyncMMDBReader.open(source)
    vi.mocked(source.read).mockClear()
    expect(await reader.get('2001:db8::1')).toBeNull()
    expect(await reader.get('::1')).toBeNull()
    expect(source.read).not.toHaveBeenCalled()
  })
})
