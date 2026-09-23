import { expect, it } from 'vitest'
import { CONNECTIONS_TABLE_ACCESSOR_KEY as KEY } from '@/constant'
import { connectionFieldOptions, normalizeConnectionFields } from '@/helper/connectionFields'

it('migrates retired stored columns and card fields while preserving supported order', async () => {
  localStorage.setItem('config/connection-table-columns', JSON.stringify([KEY.Rule, KEY.Protocol, KEY.Host, KEY.OutboundType]))
  localStorage.setItem('config/connection-card-lines', JSON.stringify([[KEY.Host, KEY.Protocol], [KEY.FromOutbound], [KEY.Chains]]))
  const settings = await import('@/store/settings')
  expect(settings.connectionTableColumns.value).toEqual([KEY.Rule, KEY.Host])
  expect(settings.connectionCardLines.value).toEqual([[KEY.Host], [KEY.Chains]])
  // Later imports/host hydration go through the same compatibility path.
  settings.connectionTableColumns.value = [KEY.FromOutbound]
  settings.connectionCardLines.value = [[KEY.Protocol]]
  expect(settings.connectionTableColumns.value).toEqual([KEY.Host])
  expect(settings.connectionCardLines.value).toEqual([[KEY.Host]])
  expect(connectionFieldOptions).not.toContain(KEY.Protocol)
  expect(connectionFieldOptions).not.toContain(KEY.OutboundType)
  expect(connectionFieldOptions).not.toContain(KEY.FromOutbound)
})

it('retires native grouping while keeping supported table grouping and pin order', async () => {
  localStorage.setItem('config/connection-card-group-key', KEY.OutboundType)
  const groups = await import('@/composables/connectionCardGroups')
  expect(groups.connectionCardGroupKey.value).toBeNull()
  expect(groups.CONNECTION_CARD_GROUPABLE_KEYS).not.toContain(KEY.Protocol)
  groups.connectionCardGroupKey.value = KEY.FromOutbound
  expect(groups.connectionCardGroupKey.value).toBeNull()
  expect(normalizeConnectionFields([KEY.Rule, KEY.OutboundType, KEY.Host])).toEqual([KEY.Rule, KEY.Host])
})
