import { expect, it, vi } from 'vitest'
import { createApp, nextTick, ref } from 'vue'
import { makeHostSnapshot } from './hostFixture'

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
}))
vi.mock('@/composables/useBackendRuntimeConfig', () => ({
  useBackendRuntimeConfig: () => ({
    configs: ref({}),
    isActiveConfigLoaded: ref(true),
    tunState: ref({ visible: false }),
    updateAllowLan: vi.fn(),
    updateTunEnabled: vi.fn(),
  }),
}))

it.each([
  [false, 'en-US'], [true, 'en-US'], [false, 'zh-CN'], [true, 'zh-CN'],
] as const)(
  'localizes Core operations without changing their order or wiring (sing-box=%s, locale=%s)',
  async (singBox, locale) => {
    const { isSingBoxCore } = await import('@/assembly/version')
    ;(isSingBoxCore as { value: boolean }).value = singBox
    const { applyHostState } = await import('@/composables/hostBridge')
    applyHostState(makeHostSnapshot({ coreUpdateAvailable: true }))
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
    app.provide(coreHostActionsKey, {
      isRunning: ref(true),
      isCoreUpgrading: ref(false),
      canUpgradeCore: ref(true),
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
      buttons.at(-1)!.click()
      expect(restartCore).toHaveBeenCalledOnce()
      expect(upgradeCore).toHaveBeenCalledOnce()
      expect(buttons.at(-1)!.parentElement!.querySelector('.indicator-item')).not.toBeNull()
      applyHostState(makeHostSnapshot({ coreUpdateAvailable: false }))
      await nextTick()
      expect(buttons.at(-1)!.parentElement!.querySelector('.indicator-item')).toBeNull()
    } finally {
      app.unmount()
      i18n.global.locale.value = previousLocale
      applyHostState(makeHostSnapshot())
    }
  },
)
