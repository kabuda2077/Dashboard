import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick } from 'vue'
import { installHostMock } from './hostFixture'

const key = 'config/proxy-folders'
const valid = {
  folders: [
    {
      id: 'custom',
      name: 'My folder',
      order: 0,
      rules: [{ type: 'regex', pattern: '^Node' }],
      manualIncludes: [],
    },
  ],
  activeId: 'custom',
  seeded: true,
}
const malformed = [
  { ...valid, folders: null },
  { ...valid, folders: [{ ...valid.folders[0], rules: null }] },
  { ...valid, folders: [{ ...valid.folders[0], manualIncludes: 'Node' }] },
  { ...valid, folders: [{ ...valid.folders[0], rules: [{ type: 'regex', pattern: null }] }] },
  { ...valid, folders: [{ ...valid.folders[0], name: 42 }] },
]
beforeEach(() => {
  vi.resetModules()
  localStorage.clear()
  sessionStorage.clear()
})
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
  document.body.replaceChildren()
})

it.each(malformed)(
  'rejects malformed nested folder imports before changing any preference (%j)',
  async (value) => {
    const { parseSettingsDocument, applyDashboardSettingsToStorage } =
      await import('@/helper/utils')
    const previous = JSON.stringify(valid)
    localStorage.setItem(key, previous)
    const preferences = { [key]: JSON.stringify(value), 'config/font': 'MiSans' }
    expect(() => parseSettingsDocument({ schemaVersion: 2, preferences })).toThrow(
      /Invalid preference value/,
    )
    expect(() => applyDashboardSettingsToStorage(preferences, true)).toThrow(
      /Invalid preference value/,
    )
    expect(localStorage.getItem(key)).toBe(previous)
    expect(localStorage.getItem('config/font')).toBeNull()
  },
)

it('preserves valid custom folders, extension fields and unknown preferences', async () => {
  const {
    parseSettingsDocument,
    applyDashboardSettingsToStorage,
    getDashboardSettingsFromStorage,
  } = await import('@/helper/utils')
  const extended = {
    ...valid,
    futureOption: true,
    folders: [
      {
        ...valid.folders[0],
        futureOption: { value: 1 },
        rules: [{ type: 'future-rule', options: ['x'] }],
      },
    ],
  }
  const preferences = {
    [key]: JSON.stringify(extended),
    'config/future-preference': 'unrecognized',
  }
  applyDashboardSettingsToStorage(parseSettingsDocument({ schemaVersion: 2, preferences }))
  expect(getDashboardSettingsFromStorage()).toEqual(preferences)
  const { folderState } = await import('@/store/proxy-folders')
  expect(folderState.value).toEqual(extended)
})

it.each([JSON.stringify(malformed[0]), JSON.stringify(malformed[1]), '{broken-json'])(
  'recovers malformed host-restored folders and renders the real folder toolbar (%s)',
  async (stored) => {
    const host = installHostMock()
    const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
    const restore = restoreDashboardSettings()
    host.bootstrap(undefined, {
      [key]: stored,
      'config/proxy-folder-mode-setting': 'on',
      'config/future-preference': 'keep',
    })
    await restore
    const { proxyGroupList, proxyMap } = await import('@/assembly/proxies')
    const folders = await import('@/store/proxy-folders')
    proxyGroupList.value = ['NodeGroup']
    proxyMap.value = {
      NodeGroup: {
        name: 'NodeGroup',
        type: 'Selector',
        all: ['DIRECT'],
        now: 'DIRECT',
        history: [],
        udp: true,
      },
    }
    const { default: Toolbar } = await import('@/components/proxies/folders/FolderTopBar.vue')
    const { i18n } = await import('@/i18n')
    const root = document.createElement('div')
    document.body.append(root)
    const app = createApp(Toolbar)
    app.use(i18n)
    app.mount(root)
    try {
      await nextTick()
      expect(folders.folders.value).toHaveLength(2)
      expect(folders.foldersOfGroup('NodeGroup')).toContain(folders.BUILTIN_NODES_ID)
      expect(root.querySelectorAll('button').length).toBeGreaterThan(1)
      await vi.waitFor(() =>
        expect(JSON.parse(localStorage.getItem(key)!)).toEqual(folders.folderState.value),
      )
      expect(localStorage.getItem('config/future-preference')).toBe('keep')
    } finally {
      app.unmount()
    }
  },
)
