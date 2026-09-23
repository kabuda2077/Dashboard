import {
  expandedConnectionCardGroupIds,
  hasConnectionCardGroups,
  hasExpandedConnectionCardGroups,
  resetConnectionCardGroups,
  syncConnectionCardGroupIds,
  toggleAllConnectionCardGroups,
  toggleConnectionCardGroup,
} from '@/composables/connectionCardGroups'
import { beforeEach, describe, expect, it } from 'vitest'

beforeEach(() => {
  resetConnectionCardGroups()
  syncConnectionCardGroupIds([])
})

describe('connection card group expansion', () => {
  it('toggles individual groups and all groups', () => {
    syncConnectionCardGroupIds(['a', 'b'])
    expect(hasConnectionCardGroups.value).toBe(true)
    expect(hasExpandedConnectionCardGroups.value).toBe(false)

    toggleConnectionCardGroup('a')
    expect([...expandedConnectionCardGroupIds.value]).toEqual(['a'])

    toggleAllConnectionCardGroups()
    expect(expandedConnectionCardGroupIds.value.size).toBe(0)

    toggleAllConnectionCardGroups()
    expect([...expandedConnectionCardGroupIds.value]).toEqual(['a', 'b'])
  })

  it('retains expansion only for groups that still exist', () => {
    syncConnectionCardGroupIds(['a', 'b'])
    toggleAllConnectionCardGroups()
    syncConnectionCardGroupIds(['b', 'c'])

    expect([...expandedConnectionCardGroupIds.value]).toEqual(['b'])
  })
})
