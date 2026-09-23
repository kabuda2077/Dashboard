import { createApp, nextTick } from 'vue'
import { expect, it, vi } from 'vitest'
const { update, notification } = vi.hoisted(() => ({ update: vi.fn(), notification: vi.fn() }))
vi.mock('@/assembly/config', () => ({ configs: { value: {} }, updateConfigs: update }))
vi.mock('@/helper/notification', () => ({ showNotification: notification }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))

it('a real port edit reports failure while an obsolete edit stays silent', async () => {
  const { default: Ports } = await import('@/components/settings/backend/BackendPortsGrid.vue')
  const { addBackend, activeUuid } = await import('@/store/setup')
  addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: '' }, { replaceExisting: true })
  const root = document.createElement('div')
  const app = createApp(Ports)
  app.config.globalProperties.$t = (key: string) => key
  app.mount(root)
  try {
    const input = root.querySelector('input')!
    update.mockRejectedValueOnce(new Error('Port already in use'))
    input.value = '7890'; input.dispatchEvent(new Event('change'))
    await vi.waitFor(() => expect(notification).toHaveBeenCalledTimes(1))
    expect(notification).toHaveBeenCalledWith(expect.objectContaining({ content: 'Port already in use', type: 'alert-error' }))
    let reject!: (error: Error) => void
    update.mockReturnValueOnce(new Promise((_resolve, fail) => { reject = fail }))
    input.dispatchEvent(new Event('change'))
    activeUuid.value = null
    reject(new Error('Old port failure'))
    await nextTick(); await nextTick()
    expect(notification).toHaveBeenCalledTimes(1)
  } finally { app.unmount() }
})
