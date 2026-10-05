import { afterEach, expect, it, vi } from 'vitest'
import { startMockHost } from './hostFixture'
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})
it('a native suspend flush waits for the newest queued preference snapshot to reach disk', async () => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  const host = await startMockHost()
  const { saveDashboardSettingsToHost, installDashboardSettingsSync } =
    await import('@/helper/dashboardSettingsSync')
  installDashboardSettingsSync()
  localStorage.setItem('config/theme', 'first')
  const first = saveDashboardSettingsToHost()
  localStorage.setItem('config/theme', 'latest')
  host.emit({ type: 'flushPreferences', requestId: 'flush-native' })
  expect(host.post).toHaveBeenCalledTimes(1)
  host.ack(host.post.mock.calls[0][0].requestId)
  await first
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.calls[1][0].preferences['config/theme']).toBe('latest')
  expect(host.post.mock.calls.some(([message]) => message.type === 'preferencesFlushed')).toBe(
    false,
  )
  host.ack(host.post.mock.calls[1][0].requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.lastCall![0]).toMatchObject({
    protocolVersion: 2,
    type: 'preferencesFlushed',
    requestId: 'flush-native',
    value: true,
  })
})
it.each(['mihomo', 'sing-box'] as const)(
  'an unsaved %s draft survives flushing and only saving or discarding permits release',
  async (kind) => {
    vi.resetModules()
    vi.useFakeTimers()
    localStorage.clear()
    const host = await startMockHost()
    const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
    const { coreDrafts, syncCoreDrafts, acceptCoreDraft, resetCoreDraft } =
      await import('@/helper/hostDraft')
    const profile = host.bridge.hostState.value.profiles![kind]
    syncCoreDrafts(host.bridge.hostState.value.profiles!)
    installDashboardSettingsSync()
    const draft = coreDrafts[kind]!
    draft.values.apiUrl = 'http://127.0.0.1:19999'
    host.emit({ type: 'flushPreferences', requestId: 'dirty' })
    await vi.advanceTimersByTimeAsync(0)
    expect(host.post.mock.lastCall![0]).toMatchObject({
      type: 'preferencesFlushed', requestId: 'dirty', value: false,
    })
    expect(draft.values.apiUrl).toBe('http://127.0.0.1:19999')
    // Accepting an acknowledged profile is different from merely sending Save.
    acceptCoreDraft(draft, { ...draft.values }, { ...profile, apiUrl: draft.values.apiUrl, revision: profile.revision + 1 })
    host.emit({ type: 'flushPreferences', requestId: 'saved' })
    await vi.advanceTimersByTimeAsync(0)
    expect(host.post.mock.lastCall![0]).toMatchObject({ requestId: 'saved', value: true })
    draft.values.configPath += '.edited'
    resetCoreDraft(kind, profile)
    host.emit({ type: 'flushPreferences', requestId: 'discarded' })
    await vi.advanceTimersByTimeAsync(0)
    expect(host.post.mock.lastCall![0]).toMatchObject({ requestId: 'discarded', value: true })
    expect(host.post.mock.calls.some(([message]) => message.type === 'saveProfile')).toBe(false)
  },
)
it('an edit made during a slow preference commit still blocks native release', async () => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  const host = await startMockHost()
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  const { coreDrafts, syncCoreDrafts } = await import('@/helper/hostDraft')
  syncCoreDrafts(host.bridge.hostState.value.profiles!)
  installDashboardSettingsSync()
  localStorage.setItem('config/theme', 'pending')
  host.emit({ type: 'flushPreferences', requestId: 'slow' })
  await vi.advanceTimersByTimeAsync(0)
  coreDrafts['sing-box']!.replaceSecret = true
  host.ack(host.post.mock.calls[0][0].requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.lastCall![0]).toMatchObject({
    type: 'preferencesFlushed', requestId: 'slow', value: false,
  })
})
it('native flush waits for local database owners before acknowledging preferences', async () => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  const host = await startMockHost()
  const { registerPersistenceFlusher } = await import('@/helper/persistenceBarrier')
  let finish!: () => void
  registerPersistenceFlusher(() => new Promise<void>((resolve) => { finish = resolve }))
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  installDashboardSettingsSync()
  host.emit({ type: 'flushPreferences', requestId: 'database' })
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post).not.toHaveBeenCalled()
  finish()
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.lastCall![0]).toMatchObject({ type: 'preferencesFlushed', requestId: 'database', value: true })
})
it('failed persistence tells the native owner not to discard the view', async () => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  const host = await startMockHost()
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  installDashboardSettingsSync()
  localStorage.setItem('config/theme', 'not-saved')
  host.emit({ type: 'flushPreferences', requestId: 'flush-failed' })
  await vi.advanceTimersByTimeAsync(0)
  host.ack(undefined, false)
  await vi.advanceTimersByTimeAsync(1)
  await vi.waitFor(() =>
    expect(host.post.mock.lastCall![0]).toMatchObject({
      type: 'preferencesFlushed',
      requestId: 'flush-failed',
      value: false,
    }),
  )
})
