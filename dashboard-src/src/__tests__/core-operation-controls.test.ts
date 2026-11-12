import { expect, it, vi } from 'vitest'
import { createApp, nextTick, ref } from 'vue'
import { makeHostSnapshot } from './hostFixture'

vi.mock('@/composables/hostBridge', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/hostBridge')>()),
  hasHostBridge: true,
  postHostMessage: vi.fn(),
}))

vi.mock('@/assembly/config', () => ({
  flushDNSCacheAPI: vi.fn(),
  flushFakeIPAPI: vi.fn(),
  reloadConfigsAPI: vi.fn(),
  updateGeoDataAPI: vi.fn(),
  fetchConfigs: vi.fn(),
}))
vi.mock('@/assembly/proxies', () => ({
  fetchProxies: vi.fn(),
  flushSmartGroupWeightsAPI: vi.fn(),
  hasSmartGroup: false,
}))
vi.mock('@/assembly/rules', () => ({ fetchRules: vi.fn() }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: ref(false) }))
vi.mock('@/components/common/BackendVersion.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/backend/TopDownloadConnections.vue', () => ({
  default: { render: () => null },
}))
vi.mock('@/helper/backendSession', () => ({
  captureBackendSession: () => ({ isCurrent: () => true }),
  backendConnectionStatus: ref('ready'),
  backendSessionReady: ref(true),
}))
vi.mock('@/composables/useBackendRuntimeConfig', () => ({
  useBackendRuntimeConfig: () => ({
    configStatus: ref('ready'),
    configError: ref(null),
    allowLanEnabled: ref(false),
    ensureConfigLoaded: vi.fn(),
    tunState: ref({ visible: false }),
    updateAllowLan: vi.fn(),
    updateTunEnabled: vi.fn(),
  }),
}))

it.each([
  [false, 'en-US', 'automatic'],
  [true, 'en-US', 'manual'],
  [false, 'zh-CN', 'automatic'],
  [true, 'zh-CN', 'manual'],
  [false, 'en-US', 'manual'],
  [false, 'zh-CN', 'manual'],
] as const)(
  'localizes and routes Core operations (sing-box=%s, locale=%s, mode=%s)',
  async (singBox, locale, mode) => {
    const { isSingBoxCore } = await import('@/assembly/version')
    ;(isSingBoxCore as { value: boolean }).value = singBox
    const { applyHostState, postHostMessage } = await import('@/composables/hostBridge')
    vi.mocked(postHostMessage).mockClear()
    const source = {
      coreUpdateMode: mode,
      coreReleaseUrl:
        mode === 'automatic'
          ? 'https://github.com/MetaCubeX/mihomo/releases'
          : singBox
            ? 'https://github.com/reF1nd/sing-box-releases/releases'
            : 'https://github.com/TanakaLun/mihomo/releases',
    }
    applyHostState(makeHostSnapshot({ coreUpdateAvailable: true, ...source }))
    const { coreHostActionsKey } = await import('@/composables/coreHostActions')
    const { default: Component } = await import('@/components/settings/backend/BackendSettings.vue')
    const app = createApp(Component)
    const { i18n } = await import('@/i18n')
    const previousLocale = i18n.global.locale.value
    i18n.global.locale.value = locale
    app.use(i18n)
    const t = i18n.global.t
    const restartCore = vi.fn(),
      upgradeCore = vi.fn()
    const isCoreUpgrading = ref(false)
    const canUpgradeCore = ref(mode === 'automatic')
    app.provide(coreHostActionsKey, {
      isRunning: ref(true),
      isCoreUpgrading,
      canUpgradeCore,
      restartCore,
      upgradeCore,
    })
    const root = document.createElement('div')
    app.mount(root)
    try {
      await nextTick()
      const buttons = [...root.querySelectorAll<HTMLButtonElement>('.setting-panel-row button')]
      expect(buttons.map((button) => button.textContent?.trim())).toEqual([
        t('reloadConfigs'),
        t('desktop.restart'),
        t('flushDNSCache'),
        t('flushFakeIP'),
        ...(!singBox ? [t('updateGeoDatabase')] : []),
        t('desktop.upgrade'),
      ])
      expect(root.textContent).toContain(locale === 'en-US' ? 'Current downloads' : '当前下载')
      expect(buttons[1].textContent).toContain(locale === 'en-US' ? 'Restart core' : '重启内核')
      buttons[1].click()
      expect(restartCore).toHaveBeenCalledOnce()
      const upgrade = buttons.at(-1)!
      expect(upgrade.disabled).toBe(false)
      upgrade.click()
      if (mode === 'manual') {
        expect(upgradeCore).not.toHaveBeenCalled()
        expect(postHostMessage).toHaveBeenLastCalledWith({ type: 'openCoreRelease' })
        expect(upgrade.title).toBe(t('desktop.manualCoreUpdateHint'))
        expect(root.querySelector('.indicator-item')).toBeNull()
      } else {
        expect(upgradeCore).toHaveBeenCalledOnce()
        expect(postHostMessage).not.toHaveBeenCalled()
        expect(upgrade.parentElement!.querySelector('.indicator-item')).not.toBeNull()
      }
      applyHostState(makeHostSnapshot({ coreUpdateAvailable: false, ...source }))
      canUpgradeCore.value = false
      await nextTick()
      expect(upgrade.parentElement!.querySelector('.indicator-item')).toBeNull()
      expect(upgrade.disabled).toBe(false)
      expect(upgrade.title).toBe(t('desktop.manualCoreUpdateHint'))
      upgradeCore.mockClear()
      upgrade.click()
      expect(upgradeCore).not.toHaveBeenCalled()
      expect(postHostMessage).toHaveBeenLastCalledWith({ type: 'openCoreRelease' })

      applyHostState(makeHostSnapshot({ coreUpdateMode: 'unknown', coreReleaseUrl: '' }))
      await nextTick()
      expect(upgrade.textContent?.trim()).toBe(t('desktop.upgrade'))
      expect(upgrade.disabled).toBe(true)
      expect(upgrade.title).toBe(t('desktop.unknownCoreSourceHint'))
      vi.mocked(postHostMessage).mockClear()
      upgrade.click()
      expect(postHostMessage).not.toHaveBeenCalled()

      applyHostState(makeHostSnapshot(source))
      isCoreUpgrading.value = true
      await nextTick()
      expect(upgrade.textContent?.trim()).toBe(t('desktop.upgrading'))
      expect(upgrade.disabled).toBe(true)
      upgrade.click()
      expect(postHostMessage).not.toHaveBeenCalled()
    } finally {
      app.unmount()
      i18n.global.locale.value = previousLocale
      applyHostState(makeHostSnapshot())
    }
  },
)
