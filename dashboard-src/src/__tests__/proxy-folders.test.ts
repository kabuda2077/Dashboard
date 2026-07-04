import type { Proxy } from '@/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const proxy = (name: string, all: string[] = []): Proxy => ({
  name,
  type: all.length ? 'Selector' : 'Direct',
  history: [],
  extra: {},
  all,
  udp: true,
  now: '',
  icon: '',
})

describe('proxy folder rules', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    vi.resetModules()
  })

  it('classifies built-in strategy and node-only groups', async () => {
    const proxies = await import('@/assembly/proxies')
    const folders = await import('@/store/proxyFolders')

    proxies.proxyGroupList.value = ['Strategy', 'NodeOnly']
    proxies.proxyMap.value = {
      Strategy: proxy('Strategy', ['NodeOnly', 'ProxyA']),
      NodeOnly: proxy('NodeOnly', ['ProxyA', 'ProxyB']),
      ProxyA: proxy('ProxyA'),
      ProxyB: proxy('ProxyB'),
    }

    expect(folders.foldersOfGroup('Strategy')).toContain(folders.BUILTIN_STRATEGY_ID)
    expect(folders.foldersOfGroup('NodeOnly')).toContain(folders.BUILTIN_NODES_ID)
  })

  it('applies regex includes, regex excludes, and manual includes', async () => {
    const proxies = await import('@/assembly/proxies')
    const folders = await import('@/store/proxyFolders')

    proxies.proxyGroupList.value = ['NodeAlpha', 'NodeBeta', 'ManualGroup']
    proxies.proxyMap.value = {
      NodeAlpha: proxy('NodeAlpha', ['ProxyA']),
      NodeBeta: proxy('NodeBeta', ['ProxyB']),
      ManualGroup: proxy('ManualGroup', ['ProxyC']),
      ProxyA: proxy('ProxyA'),
      ProxyB: proxy('ProxyB'),
      ProxyC: proxy('ProxyC'),
    }
    folders.folderState.value.folders = [
      {
        id: 'custom',
        name: 'custom',
        order: 0,
        rules: [
          { type: 'regex', pattern: '^Node' },
          { type: 'excludeRegex', pattern: 'Beta$' },
        ],
        manualIncludes: ['ManualGroup'],
      },
    ]

    expect(folders.groupMatchesFolderRule('NodeAlpha', 'custom')).toBe(true)
    expect(folders.groupMatchesFolderRule('NodeBeta', 'custom')).toBe(false)
    expect(folders.foldersOfGroup('ManualGroup')).toEqual(['custom'])
  })
})
