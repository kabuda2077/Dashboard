import Fields from '@/components/settings/CoreProfileFields.vue'
import type { CoreDraft } from '@/helper/hostDraft'
import { i18n } from '@/i18n'
import { afterEach, expect, it } from 'vitest'
import { createApp, h, nextTick, reactive, type App } from 'vue'

const draft = (secret = 'test-only-secret'): CoreDraft => ({
  base: {
    revision: 0,
    exePath: 'C:/core.exe',
    configPath: 'C:/config.yaml',
    apiUrl: 'http://127.0.0.1:9090',
    secret,
    secretDecryptionFailed: false,
  },
  values: {
    exePath: 'C:/core.exe',
    configPath: 'C:/config.yaml',
    apiUrl: 'http://127.0.0.1:9090',
    secret,
  },
  replaceSecret: false,
})
let app: App | undefined
let root: HTMLDivElement
const locale = i18n.global.locale.value
afterEach(() => {
  app?.unmount()
  root?.remove()
  i18n.global.locale.value = locale
})
const mount = () => {
  const state = reactive({ draft: draft() })
  root = document.createElement('div')
  document.body.append(root)
  app = createApp({
    render: () => h(Fields, { draft: state.draft, idPrefix: 'profile', busy: false }),
  })
  app.mount(root)
  return state
}

it.each(['zh-CN', 'en-US'] as const)(
  'reveals only on request, keeps the value and remasks when focus leaves (%s)',
  async (language) => {
    i18n.global.locale.value = language
    const state = mount()
    const input = root.querySelector<HTMLInputElement>('#profile-secret')!
    const button = root.querySelector<HTMLButtonElement>('button[aria-controls=profile-secret]')!
    expect(input.type).toBe('password')
    expect(button.getAttribute('aria-label')).toBe(i18n.global.t('desktop.showSecret'))
    button.focus()
    button.click()
    await nextTick()
    expect(input.type).toBe('text')
    expect(button.getAttribute('aria-pressed')).toBe('true')
    expect(button.getAttribute('aria-label')).toBe(i18n.global.t('desktop.hideSecret'))
    expect(input.value).toBe('test-only-secret')
    input.focus()
    await nextTick()
    expect(input.type).toBe('text') // moving between input and eye is not leaving the field
    root.querySelector<HTMLInputElement>('#profile-exe')!.focus()
    await nextTick()
    expect(input.type).toBe('password')
    expect(state.draft.values.secret).toBe('test-only-secret')
  },
)

it('does not carry visibility into another profile and respects unreadable credentials', async () => {
  const state = mount()
  const input = root.querySelector<HTMLInputElement>('#profile-secret')!
  const button = root.querySelector<HTMLButtonElement>('button[aria-controls=profile-secret]')!
  button.click()
  await nextTick()
  expect(input.type).toBe('text')
  state.draft = draft('other-secret')
  await nextTick()
  expect(input.type).toBe('password')
  expect(input.value).toBe('other-secret')
  state.draft.base.secretDecryptionFailed = true
  await nextTick()
  expect(input.disabled).toBe(true)
  expect(button.disabled).toBe(true)
  button.click()
  await nextTick()
  expect(input.type).toBe('password')
  state.draft.replaceSecret = true
  await nextTick()
  expect(input.disabled).toBe(false)
  expect(button.disabled).toBe(false)
})
