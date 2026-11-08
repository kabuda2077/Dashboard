import { afterEach, expect, it, vi } from 'vitest'
import { createApp, nextTick } from 'vue'
import { startMockHost } from './hostFixture'

vi.mock('@/api/clash', () => ({
  fetchClashVersion: vi.fn(),
  restartCoreAPI: vi.fn(),
  upgradeCoreAPI: vi.fn(),
}))

afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
  localStorage.clear()
})

it.each([
  ['TanakaLun-Alpha-9fc52a02', false, 'manual', 'TanakaLun/mihomo'],
  ['Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64', false, 'manual', 'TanakaLun/mihomo'],
  ['alpha-smart-ab12', false, 'manual', 'vernesong/mihomo'],
  ['Mihomo Smart v1.19.33 windows amd64', false, 'manual', 'vernesong/mihomo'],
  ['sing-box version 1.14.0-beta.13-reF1nd', true, 'manual', 'reF1nd/sing-box-releases'],
  ['sing-box 1.14.0-beta.13', true, 'manual', 'SagerNet/sing-box'],
  ['Mihomo Meta v1.19.33 windows amd64 with go1.26.8', false, 'automatic', 'MetaCubeX/mihomo'],
  ['alpha-9fc52a02', false, 'automatic', 'MetaCubeX/mihomo'],
  ['Alpha-abc123', false, 'automatic', 'MetaCubeX/mihomo'],
  ['1.19.33', false, 'unknown', ''],
  ['custom build with v1.19.33', false, 'unknown', ''],
  ['custom-1.19.33', false, 'unknown', ''],
  ['sing-box version 1.14.0-another-fork', true, 'unknown', ''],
  ['', false, 'unknown', ''],
] as const)(
  'resolves %s without guessing an unknown repository',
  async (value, singBox, mode, repo) => {
    const { resolveCoreUpdateSource } = await import('@/assembly/version')
    expect(resolveCoreUpdateSource(value, singBox)).toEqual({
      mode,
      releaseUrl: repo ? `https://github.com/${repo}/releases` : '',
    })
  },
)

it('uses the host source despite a different API version, opens releases, then clears an unknown link', async () => {
  vi.resetModules()
  const releaseUrl = 'https://github.com/TanakaLun/mihomo/releases'
  const host = await startMockHost({
    coreVersion: 'v1.19.33',
    coreUpdateMode: 'manual',
    coreReleaseUrl: releaseUrl,
    coreUpdateAvailable: true,
  })
  const { default: BackendVersion } = await import('@/components/common/BackendVersion.vue')
  const { coreUpdateSource, isCoreUpdateAvailable } = await import('@/assembly/version')
  const { i18n } = await import('@/i18n')
  const app = createApp(BackendVersion)
  app.use(i18n)
  const root = document.createElement('div')
  document.body.append(root)
  app.mount(root)
  try {
    const link = root.querySelector('a')!
    expect(link.href).toBe(releaseUrl)
    expect(link.title).toBe(i18n.global.t('desktop.manualCoreUpdateHint'))
    expect(coreUpdateSource.value.mode).toBe('manual')
    expect(isCoreUpdateAvailable.value).toBe(false)
    link.click()
    expect(host.post).toHaveBeenLastCalledWith(
      expect.objectContaining({ type: 'openCoreRepository' }),
    )
    expect(host.post.mock.calls.some(([message]) => message.type === 'upgradeCore')).toBe(false)
    host.runtime({ coreUpdateMode: 'unknown', coreReleaseUrl: '' })
    await nextTick()
    expect(link.hasAttribute('href')).toBe(false)
    expect(link.title).toBe(i18n.global.t('desktop.unknownCoreSourceHint'))
    link.focus()
    expect(document.activeElement).toBe(link)
    expect(link.getAttribute('aria-disabled')).toBe('true')
    expect(document.getElementById(link.getAttribute('aria-describedby')!)?.textContent).toBe(
      i18n.global.t('desktop.unknownCoreSourceHint'),
    )
    host.post.mockClear()
    link.click()
    expect(host.post).not.toHaveBeenCalled()
  } finally {
    app.unmount()
    root.remove()
  }
})

it('does not check the official repository for browser forks or unknown sources', async () => {
  vi.resetModules()
  const fetch = vi.fn()
  vi.stubGlobal('fetch', fetch)
  try {
    const { version, fetchBackendUpdateAvailableAPI } = await import('@/assembly/version')
    for (const value of ['TanakaLun-Alpha-9fc52a02', 'alpha-smart-ab12', 'custom-1.19.33']) {
      version.value = value
      expect(await fetchBackendUpdateAvailableAPI()).toBe(false)
    }
    expect(fetch).not.toHaveBeenCalled()
    fetch.mockResolvedValue({ ok: true, json: async () => ({ assets: [] }) })
    version.value = 'Alpha-abc123'
    expect(await fetchBackendUpdateAvailableAPI()).toBe(true)
    expect(fetch).toHaveBeenLastCalledWith(
      'https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/Prerelease-Alpha',
    )
    version.value = 'v1.19.33'
    expect(await fetchBackendUpdateAvailableAPI()).toBe(true)
    expect(fetch).toHaveBeenLastCalledWith(
      'https://api.github.com/repos/MetaCubeX/mihomo/releases/latest',
    )
  } finally {
    vi.unstubAllGlobals()
  }
})
