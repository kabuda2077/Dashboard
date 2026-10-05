import { afterEach, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, inject, nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { makeHostSnapshot, startMockHost } from './hostFixture'
const dialog = vi.hoisted(() => ({
  key: Symbol('core-actions'),
  resolve: undefined as undefined | ((value: { confirmed: boolean; checked: boolean }) => void),
}))
vi.mock('@/composables/coreHostActions', () => ({ coreHostActionsKey: dialog.key }))
vi.mock('@/helper/confirmDialog', () => ({
  showConfirmDialog: vi.fn(
    () =>
      new Promise((resolve) => {
        dialog.resolve = resolve
      }),
  ),
}))
vi.mock('@/components/common/CtrlsBar.vue', () => ({
  default: defineComponent({
    setup:
      (_, { slots }) =>
      () =>
        h('div', slots.default?.()),
  }),
}))
vi.mock('@/components/settings/SettingsContent.vue', () => ({
  default: defineComponent({
    setup: () => {
      const actions = inject<{ upgradeCore: () => void }>(dialog.key)!
      return () => h('button', { id: 'upgrade', onClick: () => actions.upgradeCore() }, 'upgrade')
    },
  }),
}))
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it.each(['same', 'kind', 'revision', 'epoch'])(
  'upgrade confirmation is bound to its original context (%s)',
  async (change) => {
    vi.resetModules()
    localStorage.clear()
    dialog.resolve = undefined
    const patch = {
      coreType: 'sing-box' as const,
      isRunning: true,
      canUpgradeCore: true,
      setupCompleted: true,
    }
    const initial = makeHostSnapshot(patch)
    const host = await startMockHost(patch)
    const { default: Core } = await import('@/views/CorePage.vue')
    const { i18n } = await import('@/i18n')
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/core', component: { render: () => null } }],
    })
    await router.push('/core')
    const root = document.createElement('div')
    root.id = 'app-content'
    document.body.append(root)
    const app = createApp(Core)
    app.use(router)
    app.use(i18n)
    app.mount(root)
    try {
      root.querySelector<HTMLButtonElement>('#upgrade')!.click()
      await nextTick()
      const first = host.post.mock.calls.find(([message]) => message.type === 'upgradeCore')![0]
      expect(first).toMatchObject({
        coreType: 'sing-box',
        expectedRevision: initial.profiles['sing-box'].revision,
        expectedRuntimeEpoch: initial.runtime.runtimeEpoch,
      })
      host.emit({
        type: 'commandResult',
        requestId: first.requestId,
        state: initial,
        result: { status: 'rejected', code: 'confirmationRequired' },
      })
      await vi.waitFor(() => expect(dialog.resolve).toBeTypeOf('function'))
      const changed = makeHostSnapshot(patch)
      if (change === 'kind') changed.runtime.coreType = 'mihomo'
      if (change === 'revision') changed.profiles['sing-box'].revision++
      if (change === 'epoch') changed.runtime.runtimeEpoch!++
      host.setState(changed)
      await nextTick()
      dialog.resolve!({ confirmed: true, checked: false })
      await nextTick()
      await nextTick()
      await nextTick()
      const upgrades = host.post.mock.calls.filter(([message]) => message.type === 'upgradeCore')
      expect(upgrades).toHaveLength(change === 'same' ? 2 : 1)
      if (change === 'same') {
        expect(upgrades[1][0]).toMatchObject({
          coreType: first.coreType,
          expectedRevision: first.expectedRevision,
          expectedRuntimeEpoch: first.expectedRuntimeEpoch,
          confirmUnverified: true,
        })
        host.ack(upgrades[1][0].requestId)
      }
    } finally {
      app.unmount()
      root.remove()
    }
  },
)
