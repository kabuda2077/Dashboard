import { CONNECTIONS_TABLE_ACCESSOR_KEY as KEY } from '@/constant'
import {
  connectionFieldOptions,
  normalizeConnectionCardLines,
  normalizeConnectionFields,
} from '@/helper/connectionFields'
import { expect, it } from 'vitest'

it('exposes only current fields and validates malformed field selections', () => {
  expect(connectionFieldOptions).toEqual(Object.values(KEY))
  expect(connectionFieldOptions).not.toContain('Protocol')
  expect(connectionFieldOptions).not.toContain('OutboundType')
  expect(normalizeConnectionFields([KEY.Rule, 'not-a-field', KEY.Host])).toEqual([
    KEY.Rule,
    KEY.Host,
  ])
  expect(normalizeConnectionFields(null as unknown as string[])).toEqual([])
  expect(normalizeConnectionCardLines([[KEY.Host], ['invalid' as KEY], []])).toEqual([
    [KEY.Host],
    [],
  ])
})
it('new grouping validates current choices without an old-field migration', async () => {
  const groups = await import('@/composables/connectionCardGroups')
  groups.connectionCardGroupKey.value = 'invalid' as never
  expect(groups.connectionCardGroupKey.value).toBeNull()
  groups.connectionCardGroupKey.value = KEY.Rule
  expect(groups.connectionCardGroupKey.value).toBe(KEY.Rule)
})
