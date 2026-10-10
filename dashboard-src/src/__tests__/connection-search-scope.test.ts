import { CONNECTION_DISPLAY_STYLE, CONNECTIONS_TABLE_ACCESSOR_KEY as KEY } from '@/constant'
import {
  activeConnections,
  connectionFilter,
  quickFilterEnabled,
  quickFilterRegex,
  renderConnections,
  searchHiddenColumns,
  sourceIPFilter,
} from '@/store/connections'
import {
  connectionCardLines,
  connectionDisplayStyle,
  connectionTableColumns,
} from '@/store/settings'
import type { Connection } from '@/types'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import { beforeEach, expect, it, vi } from 'vitest'

// The application entry installs this before rendering searchable time fields.
dayjs.extend(relativeTime)

vi.mock('@/api/geoip', () => ({
  getGeoIPInfoSync: () => ({ country: '', organization: '' }),
}))

const connection = (id: string, process: string, sourceIP: string) => ({
  id,
  chains: ['Proxy'],
  rule: 'Match',
  rulePayload: '',
  start: '2026-10-01T00:00:00Z',
  download: 100,
  upload: 50,
  downloadSpeed: 0,
  uploadSpeed: 0,
  metadata: {
    type: 'HTTP', network: 'tcp', host: `${id}.test`, process, sourceIP,
    sourcePort: '12345', destinationIP: '203.0.113.1', destinationPort: '443',
  },
}) as Connection

const visibleIds = () => renderConnections.value.map(({ id }) => id)

beforeEach(() => {
  connectionDisplayStyle.value = CONNECTION_DISPLAY_STYLE.TABLE
  connectionTableColumns.value = [KEY.Host]
  connectionCardLines.value = [[KEY.Host]]
  searchHiddenColumns.value = false
  quickFilterEnabled.value = false
  connectionFilter.value = ''
  sourceIPFilter.value = null
  activeConnections.value = [
    connection('a', 'only-hidden.exe', '192.0.2.1'),
    connection('b', 'other.exe', '192.0.2.2'),
  ]
})

it('searches displayed columns by default and can include hidden fields', () => {
  connectionFilter.value = 'only-hidden'
  expect(visibleIds()).toEqual([])
  searchHiddenColumns.value = true
  expect(visibleIds()).toEqual(['a'])
  searchHiddenColumns.value = false
  expect(visibleIds()).toEqual([])

  // Column selection can mutate the existing array; a reference-only cache is stale here.
  connectionTableColumns.value.push(KEY.Process)
  expect(visibleIds()).toEqual(['a'])
  connectionTableColumns.value.splice(1, 1)
  expect(visibleIds()).toEqual([])
})

it('uses the card fields when searching only displayed values', () => {
  connectionDisplayStyle.value = CONNECTION_DISPLAY_STYLE.CARD
  connectionFilter.value = 'only-hidden'
  expect(visibleIds()).toEqual([])
  searchHiddenColumns.value = true
  expect(visibleIds()).toEqual(['a'])
  searchHiddenColumns.value = false
  connectionCardLines.value = [[KEY.Host, KEY.Process]]
  expect(visibleIds()).toEqual(['a'])
})

it('applies the hide regex to all fields independently of search scope', () => {
  quickFilterEnabled.value = true
  quickFilterRegex.value = 'only-hidden'
  expect(visibleIds()).toEqual(['b'])
  searchHiddenColumns.value = true
  expect(visibleIds()).toEqual(['b'])
  sourceIPFilter.value = ['192.0.2.1']
  expect(visibleIds()).toEqual([])
})
