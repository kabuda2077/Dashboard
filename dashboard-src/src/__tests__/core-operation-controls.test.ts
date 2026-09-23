import { createApp, nextTick, ref } from 'vue'
import { expect, it, vi } from 'vitest'

vi.mock('@/assembly/config', () => ({ flushDNSCacheAPI: vi.fn(), flushFakeIPAPI: vi.fn(), reloadConfigsAPI: vi.fn(), updateGeoDataAPI: vi.fn(), fetchConfigs: vi.fn() }))
vi.mock('@/assembly/proxies', () => ({ fetchProxies: vi.fn(), flushSmartGroupWeightsAPI: vi.fn(), hasSmartGroup: false }))
vi.mock('@/assembly/rules', () => ({ fetchRules: vi.fn() }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: ref(false) }))
vi.mock('@/components/common/BackendVersion.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/backend/TopDownloadConnections.vue', () => ({ default: { render: () => null } }))
vi.mock('@/helper/backendSession', () => ({ captureBackendSession: () => ({ isCurrent: () => true }) }))
vi.mock('@/composables/useBackendRuntimeConfig', () => ({ useBackendRuntimeConfig: () => ({ configs: ref({}), isActiveConfigLoaded: ref(true), tunState: ref({ visible: false }), updateAllowLan: vi.fn(), updateTunEnabled: vi.fn() }) }))

it.each([false, true])('preserves Core operation order and host update indicator (sing-box=%s)', async singBox => {
  const { isSingBoxCore } = await import('@/assembly/version')
  ;(isSingBoxCore as { value: boolean }).value = singBox
  const { applyHostState } = await import('@/composables/hostBridge')
  applyHostState({ coreUpdateAvailable: true })
  const { coreHostActionsKey } = await import('@/composables/coreHostActions')
  const { default: Component } = await import('@/components/settings/backend/BackendSettings.vue')
  const app = createApp(Component)
  app.config.globalProperties.$t = (key: string) => key
  const restartCore = vi.fn(), upgradeCore = vi.fn()
  app.provide(coreHostActionsKey, { isRunning: ref(true), isCoreUpgrading: ref(false), canUpgradeCore: ref(true), restartCore, upgradeCore })
  const root = document.createElement('div')
  app.mount(root)
  try {
    await nextTick()
    const buttons = [...root.querySelectorAll<HTMLButtonElement>('.setting-panel-row button')]
    expect(buttons.map(button => button.textContent?.trim())).toEqual([
      'reloadConfigs', '重启内核', 'flushDNSCache', 'flushFakeIP', ...(!singBox ? ['updateGeoDatabase'] : []), '升级内核',
    ])
    buttons[1].click(); buttons.at(-1)!.click()
    expect(restartCore).toHaveBeenCalledOnce()
    expect(upgradeCore).toHaveBeenCalledOnce()
    expect(buttons.at(-1)!.parentElement!.querySelector('.indicator-item')).not.toBeNull()
    applyHostState({ coreUpdateAvailable: false }); await nextTick()
    expect(buttons.at(-1)!.parentElement!.querySelector('.indicator-item')).toBeNull()
  } finally { app.unmount(); applyHostState({}) }
})
