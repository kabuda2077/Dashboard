import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { makeHostSnapshot, startMockHost } from './hostFixture'

vi.mock('@/components/settings/SettingsContent.vue', () => ({ default: { template: '<div />' } }))
vi.mock('@/components/common/CtrlsBar.vue', () => ({
  default: { template: '<div><slot /></div>' },
}))
vi.mock('@/helper/confirmDialog', () => ({ showConfirmDialog: vi.fn() }))
let app: App | undefined
let root: HTMLDivElement
beforeEach(() => {
  vi.resetModules()
  localStorage.clear()
})
afterEach(() => {
  app?.unmount()
  root?.remove()
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
const mount = async (patch = {}) => {
  const host = await startMockHost(patch)
  const { i18n } = await import('@/i18n')
  i18n.global.locale.value = 'zh-CN'
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/core', component: { template: '<div/>' } }],
  })
  await router.push('/core')
  const { default: Core } = await import('@/views/CorePage.vue')
  root = document.createElement('div')
  document.body.append(root)
  app = createApp(Core)
  app.use(router)
  app.mount(root)
  await nextTick()
  return host
}
const edit = async (id: string, value: string) => {
  const input = root.querySelector<HTMLInputElement>(id)!
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await nextTick()
  return input
}
it('Core preserves edits through runtime messages and does not submit them with a Windows option', async () => {
  const host = await mount({ setupCompleted: true })
  const input = await edit('#core-exe', 'C:/typing.exe')
  host.runtime({ coreVersion: 'new metadata' })
  await nextTick()
  expect(input.value).toBe('C:/typing.exe')
  const option = [...root.querySelectorAll('label')]
    .find((label) => label.textContent?.includes('关闭到托盘'))!
    .querySelector<HTMLInputElement>('input')!
  option.checked = false
  option.dispatchEvent(new Event('change', { bubbles: true }))
  await nextTick()
  const request = host.post.mock.lastCall![0]
  expect(request).toMatchObject({
    type: 'setDesktopOption',
    option: 'minimizeToTray',
    value: false,
  })
  expect(request).not.toHaveProperty('draft')
  host.ack()
  await nextTick()
  expect(input.value).toBe('C:/typing.exe')
})
it('file selection fills a draft but never saves it implicitly', async () => {
  const host = await mount({ setupCompleted: true })
  const choose = [...root.querySelectorAll('button')].find(
    (button) => button.textContent === '选择',
  )!
  choose.click()
  await nextTick()
  const request = host.post.mock.lastCall![0]
  expect(request.type).toBe('chooseCoreFile')
  host.emit({
    type: 'commandResult',
    requestId: request.requestId,
    state: makeHostSnapshot({ setupCompleted: true }),
    result: { status: 'completed', code: 'fileSelected', path: 'C:/selected.exe' },
  })
  await vi.waitFor(() =>
    expect(root.querySelector<HTMLInputElement>('#core-exe')!.value).toBe('C:/selected.exe'),
  )
  expect(host.post.mock.calls.some(([message]) => message.type === 'saveProfile')).toBe(false)
})
it('first-time setup is a full dialog, preserves two drafts and waits for successful API readiness', async () => {
  const host = await mount({ setupCompleted: false, isRunning: false, apiStatus: 'idle' })
  expect(root.querySelector('[data-testid=setup-guide][role=dialog]')).not.toBeNull()
  await edit('#core-exe', 'C:/mihomo-draft.exe')
  const tab = [...root.querySelectorAll<HTMLButtonElement>('[data-testid=setup-guide] button')].find(button => button.textContent === 'sing-box')!
  tab.click(); await nextTick()
  await edit('#core-exe', 'C:/sing-box-draft.exe')
  expect(root.textContent).toContain('experimental.clash_api.external_controller')
  expect(host.post.mock.calls.some(([request]) => request.type === 'start')).toBe(false)
  const start = [...root.querySelectorAll<HTMLButtonElement>('[data-testid=setup-guide] button')].find(button => button.textContent?.trim() === '启动内核')!
  start.click(); await nextTick()
  const request = host.post.mock.calls.find(([request]) => request.type === 'start')![0]
  expect(request).toMatchObject({ coreType: 'sing-box', expectedRevision: 0, draft: { exePath: 'C:/sing-box-draft.exe' } })
  const checking = makeHostSnapshot({ coreType: 'sing-box', setupCompleted: false, apiStatus: 'checking' })
  checking.profiles['sing-box'] = { ...checking.profiles['sing-box'], revision: 1, exePath: 'C:/sing-box-draft.exe' }
  host.ack(request.requestId, true, checking)
  await nextTick(); await Promise.resolve()
  expect(host.post.mock.calls.some(([request]) => request.type === 'completeSetup')).toBe(false)
  host.runtime({ apiStatus: 'ready' })
  await vi.waitFor(() => expect(host.post.mock.calls.some(([request]) => request.type === 'completeSetup')).toBe(true))
  const finish = host.post.mock.calls.find(([request]) => request.type === 'completeSetup')![0]
  expect(finish).not.toHaveProperty('draft')
  host.ack(finish.requestId, false)
  await nextTick(); await Promise.resolve()
  expect(root.querySelector('[data-testid=setup-guide]')).not.toBeNull()
  const complete = [...root.querySelectorAll<HTMLButtonElement>('[data-testid=setup-guide] button')].find(button => button.textContent?.trim() === '完成设置')!
  await vi.waitFor(() => expect(complete.disabled).toBe(false))
  complete.click(); await nextTick()
  const completed = { ...checking, setupCompleted: true }
  host.ack(host.post.mock.calls.findLast(([request]) => request.type === 'completeSetup')![0].requestId, true, completed)
  await vi.waitFor(() => expect(root.querySelector('[data-testid=setup-guide]')).toBeNull())
  const { coreDrafts } = await import('@/helper/hostDraft')
  expect(coreDrafts.mihomo!.values.exePath).toBe('C:/mihomo-draft.exe')
})

it('setup failure remains in the dialog and never declares setup complete', async () => {
  const host = await mount({ setupCompleted: false, isRunning: false, apiStatus: 'idle' })
  await edit('#core-exe', 'C:/invalid.exe')
  const start = [...root.querySelectorAll<HTMLButtonElement>('[data-testid=setup-guide] button')].find(button => button.textContent?.trim() === '启动内核')!
  start.click(); await nextTick()
  const request = host.post.mock.calls.find(([request]) => request.type === 'start')![0]
  host.ack(request.requestId, false)
  await vi.waitFor(() => expect(start.disabled).toBe(false))
  const { i18n } = await import('@/i18n')
  expect(root.querySelector('[data-testid=core-dialog-notice]')!.textContent).toContain(i18n.global.t('desktop.result.saveFailed'))
  expect(root.querySelector('[data-testid=setup-guide]')).not.toBeNull()
  expect(root.querySelector<HTMLInputElement>('#core-exe')!.value).toBe('C:/invalid.exe')
  expect(host.post.mock.calls.some(([request]) => request.type === 'completeSetup')).toBe(false)
})

it.each([false, true])('discarding a draft requires confirmation (%s) and never writes the profile', async (confirmed) => {
  const host = await mount({ setupCompleted: true })
  const { showConfirmDialog } = await import('@/helper/confirmDialog')
  vi.mocked(showConfirmDialog).mockResolvedValue({ confirmed, checked: false })
  const original = root.querySelector<HTMLInputElement>('#core-exe')!.value
  await edit('#core-exe', 'C:/unsaved.exe')
  root.querySelector<HTMLButtonElement>('[data-testid=discard-core-profile]')!.click()
  await vi.waitFor(() => expect(showConfirmDialog).toHaveBeenCalled())
  await nextTick()
  expect(root.querySelector<HTMLInputElement>('#core-exe')!.value).toBe(confirmed ? original : 'C:/unsaved.exe')
  expect(host.post.mock.calls.some(([message]) => message.type === 'saveProfile')).toBe(false)
})
it('a delayed discard cannot reset the target after opening the switch editor', async () => {
  await mount({ setupCompleted: true })
  const { showConfirmDialog } = await import('@/helper/confirmDialog')
  let confirm!: (value: { confirmed: boolean; checked: boolean }) => void
  vi.mocked(showConfirmDialog).mockImplementation(() => new Promise(resolve => { confirm = resolve }))
  await edit('#core-exe', 'C:/unsaved.exe')
  root.querySelector<HTMLButtonElement>('[data-testid=discard-core-profile]')!.click()
  await openSwitch()
  await edit('#core-exe', 'C:/other-draft.exe')
  confirm({ confirmed: true, checked: false }); await nextTick()
  const { coreDrafts } = await import('@/helper/hostDraft')
  expect(coreDrafts.mihomo!.values.exePath).toBe('C:/unsaved.exe')
  expect(coreDrafts['sing-box']!.values.exePath).toBe('C:/other-draft.exe')
})
it('acknowledgement canonicalizes submitted fields but preserves edits made while saving', async () => {
  const { coreDrafts, syncCoreDrafts, acceptCoreDraft } = await import('@/helper/hostDraft')
  const initial = makeHostSnapshot().profiles
  syncCoreDrafts(initial)
  const draft = coreDrafts.mihomo!
  draft.values.exePath = ' C:/new.exe '
  const submitted = { ...draft.values }
  draft.values.apiUrl = 'http://localhost:9091'
  const canonical = { ...initial.mihomo, exePath: 'C:/new.exe', revision: 1 }
  acceptCoreDraft(draft, submitted, canonical)
  expect(draft.values.exePath).toBe('C:/new.exe')
  expect(draft.values.apiUrl).toBe('http://localhost:9091')
  expect(draft.base.revision).toBe(1)
})

const openSwitch = async () => {
  root.querySelector<HTMLButtonElement>('.core-top-button.btn-primary')!.click()
  await nextTick()
}
const confirmSwitch = () => root.querySelector<HTMLButtonElement>('[data-testid=switch-core-dialog] .btn-primary')!.click()

it('runtime toolbar has switch/start/stop and only the switch dialog edits the other profile', async () => {
  const host = await mount({ setupCompleted: true })
  expect([...root.querySelectorAll('.core-runtime-actions button')].map(button => button.textContent?.trim())).toEqual(['切换', '启动', '停止'])
  expect(root.querySelector('.core-toolbar select')).toBeNull()
  expect(root.querySelector<HTMLButtonElement>('.btn-success')!.disabled).toBe(true)
  const status = root.querySelector('.core-status-box')!.textContent
  await edit('#core-exe', 'C:/mihomo-draft.exe')
  await openSwitch()
  await edit('#core-exe', 'C:/sing-box-draft.exe')
  expect(root.querySelector('.core-status-box')!.textContent).toBe(status)
  expect(host.post.mock.calls.some(([request]) => ['switchCore', 'saveProfile'].includes(request.type))).toBe(false)
  root.querySelector<HTMLButtonElement>('[data-testid=switch-core-dialog] .modal-action .dashboard-action-btn')!.click()
  await nextTick()
  expect(root.querySelector<HTMLInputElement>('#core-exe')!.value).toBe('C:/mihomo-draft.exe')
  const { coreDrafts } = await import('@/helper/hostDraft')
  expect(coreDrafts['sing-box']!.values.exePath).toBe('C:/sing-box-draft.exe')
})

it('the compact status uses the lamp for readiness and keeps details in its tooltip', async () => {
  const host = await mount({ setupCompleted: true, coreTitle: 'Mihomo Core', isRunning: true, processId: 12345, apiStatus: 'ready' })
  const status = root.querySelector<HTMLElement>('.core-status-box')!
  const lamp = root.querySelector<HTMLElement>('.core-runtime-dot')!
  expect(status.textContent?.trim()).toMatch(/^Mihomo Core\s+PID 12345$/)
  expect(status.textContent).not.toMatch(/运行中|已就绪/)
  expect(lamp.classList.contains('bg-success')).toBe(true)
  expect(lamp.getAttribute('aria-label')).toBe(status.title)
  expect(lamp.title).toContain('已就绪')
  for (const apiStatus of ['checking', 'unauthorized', 'unreachable', 'idle'] as const) {
    host.runtime({ apiStatus }); await nextTick()
    expect(lamp.classList.contains('bg-warning')).toBe(true)
    expect(lamp.title).not.toContain('已就绪')
    expect(status.textContent?.trim()).toMatch(/^Mihomo Core\s+PID 12345$/)
  }
  host.runtime({ isRunning: false, processId: null, apiStatus: 'ready' }); await nextTick()
  expect(lamp.classList.contains('bg-warning')).toBe(true)
  expect(status.textContent?.trim()).toBe('Mihomo Core')
  expect(lamp.title).toContain('未运行')
  expect(root.querySelector('.core-output-panel')?.parentElement?.textContent).toContain('内核日志')
})

it('switch submits its target draft and revision; ACK preserves later and other-slot edits', async () => {
  const host = await mount({ setupCompleted: true })
  await edit('#core-exe', 'C:/other-slot.exe')
  await openSwitch()
  await edit('#core-exe', ' C:/target.exe ')
  expect(root.querySelector('[data-testid=switch-core-dialog]')!.textContent).toContain('保存 sing-box 当前草稿')
  confirmSwitch()
  await vi.waitFor(() => expect(host.post.mock.calls.some(([request]) => request.type === 'switchCore')).toBe(true))
  const request = host.post.mock.calls.find(([request]) => request.type === 'switchCore')![0]
  expect(request).toMatchObject({ coreType: 'sing-box', expectedRevision: 0, draft: { exePath: ' C:/target.exe ' } })
  await edit('#core-exe', 'C:/later-edit.exe')
  const snapshot = makeHostSnapshot({ coreType: 'sing-box', setupCompleted: true, runtimeEpoch: 2 })
  snapshot.profiles['sing-box'] = { ...snapshot.profiles['sing-box'], exePath: 'C:/target.exe', revision: 1 }
  host.ack(request.requestId, true, snapshot)
  const { coreDrafts } = await import('@/helper/hostDraft')
  await vi.waitFor(() => expect(root.querySelector('[data-testid=switch-core-dialog]')).toBeNull())
  expect(coreDrafts['sing-box']!.base.revision).toBe(1)
  expect(coreDrafts['sing-box']!.values.exePath).toBe('C:/later-edit.exe')
  expect(coreDrafts.mihomo!.values.exePath).toBe('C:/other-slot.exe')
})

it.each([false, true])('reload confirmation stays inside the native switch dialog (confirm=%s)', async confirmed => {
  const host = await mount({ setupCompleted: true })
  await openSwitch()
  await edit('#core-exe', 'C:/keep-unless-confirmed.exe')
  const next = makeHostSnapshot({ setupCompleted: true })
  next.profiles['sing-box'] = { ...next.profiles['sing-box'], revision: 1, exePath: 'C:/remote.exe' }
  host.setState(next)
  await nextTick()
  const { showConfirmDialog } = await import('@/helper/confirmDialog')
  vi.mocked(showConfirmDialog).mockClear()
  root.querySelector<HTMLButtonElement>('[data-testid=reload-dialog-draft]')!.click()
  await nextTick()
  const prompt = root.querySelector('[data-testid=reload-draft-confirmation]')!
  expect(prompt.closest('dialog')).not.toBeNull()
  expect(showConfirmDialog).not.toHaveBeenCalled()
  prompt.querySelectorAll<HTMLButtonElement>('button')[confirmed ? 1 : 0]!.click()
  await nextTick()
  expect(root.querySelector<HTMLInputElement>('#core-exe')!.value).toBe(confirmed ? 'C:/remote.exe' : 'C:/keep-unless-confirmed.exe')
  expect(host.post.mock.calls.some(([request]) => request.type === 'saveProfile' || request.type === 'switchCore')).toBe(false)
})

it('a newer remote revision cannot be discarded by an older inline confirmation', async () => {
  const host = await mount({ setupCompleted: true })
  await openSwitch()
  await edit('#core-exe', 'C:/draft.exe')
  const next = makeHostSnapshot({ setupCompleted: true })
  next.profiles['sing-box'].revision = 1
  host.setState(next); await nextTick()
  root.querySelector<HTMLButtonElement>('[data-testid=reload-dialog-draft]')!.click()
  await nextTick()
  host.setState({ ...next, profiles: { ...next.profiles, 'sing-box': { ...next.profiles['sing-box'], revision: 2 } } })
  await nextTick()
  root.querySelector<HTMLButtonElement>('[data-testid=reload-draft-confirmation] .btn-warning')!.click()
  await nextTick()
  expect(root.querySelector<HTMLInputElement>('#core-exe')!.value).toBe('C:/draft.exe')
})

it.each(['epoch', 'revision', 'cancel'])('switch dialog rejects expired or cancelled context (%s)', async change => {
  const host = await mount({ setupCompleted: true })
  await openSwitch()
  if (change === 'epoch') host.runtime({ runtimeEpoch: 2 })
  if (change === 'revision') {
    const snapshot = makeHostSnapshot({ setupCompleted: true })
    snapshot.profiles['sing-box'].revision += 1
    host.setState(snapshot)
  }
  await nextTick()
  if (change === 'cancel') root.querySelector<HTMLButtonElement>('[data-testid=switch-core-dialog] .modal-action .dashboard-action-btn')!.click()
  else confirmSwitch()
  await nextTick(); await Promise.resolve()
  expect(host.post.mock.calls.some(([request]) => request.type === 'switchCore')).toBe(false)
})
