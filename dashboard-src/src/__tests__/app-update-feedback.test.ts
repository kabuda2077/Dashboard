import { describe, expect, it } from 'vitest'
import { createApp, nextTick } from 'vue'
import { getAppUpdateFeedback } from '@/components/settings/general/appUpdateFeedback'
import { makeHostSnapshot, startMockHost } from './hostFixture'

describe('structured app update feedback', () => {
  it.each([
    [{ type: 'appUpdateResult', result: 'available', manual: true, latestVersion: '1.3.0' }, '发现 v1.3.0'],
    [{ type: 'appUpdateResult', result: 'upToDate', manual: true, currentVersion: '1.2.0' }, '已是最新版本 v1.2.0'],
    [{ type: 'appUpdateResult', result: 'failed', manual: true }, '检查失败，请稍后重试'],
    [{ type: 'appUpdateResult', result: 'busy', manual: true }, '正在检查，请稍候'],
  ] as const)('maps %s without parsing notice text', (message, expected) => {
    expect(getAppUpdateFeedback(message)).toBe(expected)
  })

  it('announces loading and confirmed update results without replacing the controls', async () => {
    const host = await startMockHost({ isAppUpdateChecking: false, appUpdateAvailable: false })
    const { default: About } = await import('@/components/settings/general/AboutDashboardSettings.vue')
    const app = createApp(About)
    const root = document.createElement('div')
    document.body.append(root)
    app.mount(root)
    try {
      const status = root.querySelector('[role=status]')!
      const check = root.querySelector<HTMLButtonElement>('[title="检查 Dashboard 更新"]')!
      expect(status.getAttribute('aria-live')).toBe('polite')
      check.click()
      expect(host.post).toHaveBeenLastCalledWith(expect.objectContaining({ type: 'checkAppUpdate' }))
      host.setState(makeHostSnapshot({ isAppUpdateChecking: true }))
      await nextTick()
      expect(status.textContent).toContain('正在检查')
      expect(check.disabled).toBe(true)

      host.setState(makeHostSnapshot({ isAppUpdateChecking: false }))
      host.emit({ type: 'appUpdateResult', result: 'failed', manual: true })
      await nextTick()
      expect(status.textContent).toContain('检查失败')
      expect(check.disabled).toBe(false)
      expect(root.querySelector('[title="检查 Dashboard 更新"]')).toBe(check)
    } finally {
      app.unmount()
      root.remove()
      window.dispatchEvent(new Event('pagehide'))
      Reflect.deleteProperty(window, 'chrome')
    }
  })

  it('ignores automatic results and ordinary notices', () => {
    expect(getAppUpdateFeedback({
      type: 'appUpdateResult',
      result: 'available',
      manual: false,
      latestVersion: '1.3.0',
    })).toBeUndefined()
    expect(getAppUpdateFeedback({
      type: 'notice',
      message: '发现 Dashboard 新版本 v9.9.9',
    })).toBeUndefined()
  })
})
