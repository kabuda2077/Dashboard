import { probeClashChannel } from '@/api/clash'
import { isBackendAvailable, probeBackend } from '@/assembly/backend'
import EditBackend from '@/components/settings/backend/EditBackendModal.vue'
import { LANG } from '@/constant'
import { captureBackendSession } from '@/helper/backendSession'
import {
  describeConnectionError,
  describeProbeFailure,
  diagnoseConnection,
  getBackendProbeUrl,
  isLoopbackHost,
} from '@/helper/connectivity'
import { showNotification } from '@/helper/notification'
import { notifyRequestErrorForSession } from '@/helper/request-error'
import { i18n } from '@/i18n'
import { activeBackend, addBackend, updateBackend } from '@/store/setup'
import type { Backend } from '@/types'
import SetupPage from '@/views/SetupPage.vue'
import { AxiosError, AxiosHeaders } from 'axios'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref } from 'vue'
import { createI18n } from 'vue-i18n'
vi.mock('@/router', async () => {
  const { ref } = await import('vue')
  return {
    default: {
      currentRoute: ref({ query: { setupMode: 'manual' } }),
      push: vi.fn(),
      replace: vi.fn(),
    },
  }
})
vi.mock('@/helper/auto-import-settings', () => ({
  syncSettingsFromCore: vi.fn().mockResolvedValue(false),
}))
vi.mock('@/components/common/DashboardSettings.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/general/LanguageSelect.vue', () => ({
  default: { render: () => null },
}))
vi.mock('@/assembly/backend', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/assembly/backend')>()),
  probeBackend: vi.fn(),
}))
vi.mock('@/helper/notification', () => ({ showNotification: vi.fn() }))
vi.mock('@/components/common/DialogWrapper.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      setup:
        (_, { slots }) =>
        () =>
          h('div', slots.default?.()),
    }),
  }
})
vi.mock('@/store/setup', async () => {
  const { ref } = await import('vue')
  return {
    activeBackend: ref({
      uuid: 'a',
      type: 'clash',
      protocol: 'http',
      host: 'a.example',
      port: '9090',
      password: 'test-secret',
      secondaryPath: '/api/',
    }),
    activeUuid: ref('a'),
    addBackend: vi.fn(),
    removeBackend: vi.fn(),
    backendList: ref(
      ['a', 'b'].map((uuid) => ({
        uuid,
        type: 'clash',
        protocol: 'http',
        host: uuid + '.example',
        port: '9090',
        password: '',
        label: uuid,
      })),
    ),
    updateBackend: vi.fn(),
  }
})
beforeEach(() => {
  i18n.global.locale.value = LANG.EN_US
})
afterEach(() => {
  vi.clearAllMocks()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})
it.each(['same', 'slot', 'draft', 'closed'])(
  'late browser probe respects the original editor context (%s)',
  async (scenario) => {
    let finish!: (value: Awaited<ReturnType<typeof probeBackend>>) => void
    vi.mocked(probeBackend).mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const visible = ref(true)
    const root = document.createElement('div')
    root.id = 'app-content'
    document.body.append(root)
    const app = createApp({
      render: () =>
        h(EditBackend, {
          modelValue: visible.value,
          'onUpdate:modelValue': (value: boolean) => {
            visible.value = value
          },
        }),
    })
    app.use(
      createI18n({
        legacy: false,
        locale: 'en',
        messages: { en: {} },
        missingWarn: false,
        fallbackWarn: false,
      }),
    )
    app.mount(root)
    try {
      await nextTick()
      const save = [...root.querySelectorAll<HTMLButtonElement>('button')].find(
        (button) => button.textContent?.trim() === 'save',
      )!
      save.click()
      await nextTick()
      expect(probeBackend).toHaveBeenCalledOnce()
      if (scenario === 'slot') {
        const select = root.querySelector('select')!
        select.value = 'b'
        select.dispatchEvent(new Event('change', { bubbles: true }))
      }
      if (scenario === 'draft') {
        const input = root.querySelector('input')!
        input.value = 'new-edit.example'
        input.dispatchEvent(new Event('input', { bubbles: true }))
      }
      if (scenario === 'closed') visible.value = false
      await nextTick()
      const probeSignal = vi.mocked(probeBackend).mock.calls[0]![2]!
      expect(probeSignal.aborted).toBe(scenario !== 'same')
      finish({ ok: true })
      await nextTick()
      await nextTick()
      await nextTick()
      if (scenario === 'same')
        expect(updateBackend).toHaveBeenCalledWith(
          'a',
          expect.objectContaining({ host: 'a.example' }),
        )
      else expect(updateBackend).not.toHaveBeenCalled()
      if (scenario === 'draft') expect(root.querySelector('input')!.value).toBe('new-edit.example')
    } finally {
      app.unmount()
      root.remove()
    }
  },
)

it('reports browser edit 401 without saving the rejected credentials', async () => {
  vi.mocked(probeBackend).mockResolvedValue({ ok: false, kind: 'unauthorized' })
  const root = document.createElement('div')
  document.body.append(root)
  const app = createApp({ render: () => h(EditBackend, { modelValue: true }) })
  app.use(
    createI18n({
      legacy: false,
      locale: 'en',
      messages: { en: {} },
      missingWarn: false,
      fallbackWarn: false,
    }),
  )
  app.mount(root)
  try {
    await nextTick()
    ;[...root.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'save')!
      .click()
    await nextTick()
    await nextTick()
    expect(updateBackend).not.toHaveBeenCalled()
    await vi.waitFor(() =>
      expect(showNotification).toHaveBeenCalledWith({
        content: i18n.global.t('diagnosisUnauthorized'),
        type: 'alert-error',
      }),
    )
  } finally {
    app.unmount()
    root.remove()
  }
})

it.each(['unauthorized', 'http'] as const)(
  'reports browser setup %s without saving credentials',
  async (kind) => {
    vi.mocked(probeBackend).mockResolvedValue({
      ok: false,
      kind,
      message: kind === 'http' ? 'HTTP 404' : undefined,
    })
    const root = document.createElement('div')
    root.id = 'app-content'
    document.body.append(root)
    const app = createApp(SetupPage)
    app.use(
      createI18n({
        legacy: false,
        locale: 'en',
        messages: { en: {} },
        missingWarn: false,
        fallbackWarn: false,
      }),
    )
    app.mount(root)
    try {
      await nextTick()
      ;[...root.querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.trim() === 'submit')!
        .click()
      await nextTick()
      await nextTick()
      expect(addBackend).not.toHaveBeenCalled()
      await vi.waitFor(() =>
        expect(showNotification).toHaveBeenCalledWith({
          content:
            kind === 'unauthorized'
              ? i18n.global.t('diagnosisUnauthorized')
              : `HTTP 404 — ${i18n.global.t('diagnosisBadEndpoint')}`,
          type: 'alert-error',
        }),
      )
    } finally {
      app.unmount()
      root.remove()
    }
  },
)

it.each([200, 401, 403, 500])(
  'classifies Clash-compatible browser probe HTTP %s',
  async (status) => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: status === 200, status })
    vi.stubGlobal('fetch', fetchMock)
    const backend = {
      protocol: 'http',
      host: 'example.com',
      port: '9090',
      password: '',
      type: 'clash',
    } as Backend
    const result = await probeClashChannel(backend, 10000)
    expect(result).toEqual(
      status === 200
        ? { ok: true }
        : { ok: false, kind: status === 401 ? 'unauthorized' : 'http', message: `HTTP ${status}` },
    )
    expect(fetchMock).toHaveBeenCalledWith(
      'http://example.com:9090/version',
      expect.objectContaining({ headers: {} }),
    )
    expect(await isBackendAvailable(backend)).toBe(status === 200)
    backend.password = 'test-secret'
    await probeClashChannel(backend, 10000)
    expect(fetchMock).toHaveBeenLastCalledWith(
      'http://example.com:9090/version',
      expect.objectContaining({ headers: { Authorization: 'Bearer test-secret' } }),
    )
  },
)

it('keeps network and timeout failures separate from bad credentials', async () => {
  const backend = {
    protocol: 'http',
    host: 'example.com',
    port: '9090',
    password: '',
    type: 'clash',
  } as Backend
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network')))
  expect(await probeClashChannel(backend, 10000)).toEqual({ ok: false, kind: 'network' })
  vi.useFakeTimers()
  vi.stubGlobal(
    'fetch',
    vi.fn(
      (_url, options) =>
        new Promise((_resolve, reject) => {
          options.signal.addEventListener('abort', () => reject(new Error('aborted')))
        }),
    ),
  )
  const result = probeClashChannel(backend, 10)
  await vi.advanceTimersByTimeAsync(10)
  expect(await result).toEqual({ ok: false, kind: 'timeout' })
  expect(vi.getTimerCount()).toBe(0)
})

it.each([
  [false, 'http:', 'http://example.com/version', false, 'offline'],
  [true, 'http:', 'http://example.com/version', true, 'corsBlocked'],
  [true, 'https:', 'http://example.com/version', false, 'mixedContent'],
  [true, 'https:', 'http://localhost/version', false, 'mixedContentOrUnreachable'],
  [true, 'https:', 'http://127.0.0.2/version', false, 'mixedContentOrUnreachable'],
  [true, 'https:', 'http://[::1]/version', false, 'mixedContentOrUnreachable'],
  [true, 'https:', 'https://example.com/version', false, 'unreachable'],
  [true, 'http:', 'http://example.com/version', false, 'unreachable'],
] as const)(
  'diagnoses online=%s page=%s url=%s responds=%s as %s',
  async (online, protocol, url, responds, expected) => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(online)
    vi.stubGlobal('location', { protocol })
    const fetchMock = responds
      ? vi.fn().mockResolvedValue({ type: 'opaque' })
      : vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))
    vi.stubGlobal('fetch', fetchMock)
    expect(await diagnoseConnection(url)).toBe(expected)
    if (online)
      expect(fetchMock).toHaveBeenCalledWith(
        url,
        expect.objectContaining({ mode: 'no-cors', cache: 'no-store', credentials: 'omit' }),
      )
    else expect(fetchMock).not.toHaveBeenCalled()
  },
)

it('derives the version probe under the API path without query or URL credentials', () => {
  const backend = {
    protocol: 'http',
    host: 'user:password@example.com',
    port: '9090',
    secondaryPath: '/api/?token=secret#fragment',
  }
  expect(getBackendProbeUrl(backend)).toBe('http://example.com:9090/api/version')
  expect(['localhost', 'local.localhost', '127.1.2.3', '::1', '[::1]'].every(isLoopbackHost)).toBe(
    true,
  )
  expect(isLoopbackHost('localhost.example.com')).toBe(false)
})

it('bounds secondary diagnostics at five seconds without forwarding Secret or cookies', async () => {
  vi.useFakeTimers()
  const fetchMock = vi.fn(
    (_url, options) =>
      new Promise((_resolve, reject) => {
        options.signal.addEventListener('abort', () =>
          reject(new DOMException('aborted', 'AbortError')),
        )
      }),
  )
  vi.stubGlobal('fetch', fetchMock)
  vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true)
  const pending = diagnoseConnection('http://example.com/api/version')
  await vi.advanceTimersByTimeAsync(4999)
  expect(fetchMock.mock.calls[0]![1].signal.aborted).toBe(false)
  await vi.advanceTimersByTimeAsync(1)
  expect(await pending).toBe('unreachable')
  expect(fetchMock.mock.calls[0]![1]).toMatchObject({ credentials: 'omit', mode: 'no-cors' })
  expect(fetchMock.mock.calls[0]![1]).not.toHaveProperty('headers')
  expect(vi.getTimerCount()).toBe(0)
})

it.each(['primary', 'secondary'] as const)(
  'does not start an already cancelled %s probe',
  async (kind) => {
    const controller = new AbortController()
    controller.abort()
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const pending =
      kind === 'primary'
        ? probeClashChannel(
            { protocol: 'http', host: 'example.com', port: '9090' } as Backend,
            10000,
            controller.signal,
          )
        : diagnoseConnection('http://example.com/version', controller.signal)
    await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
    expect(fetchMock).not.toHaveBeenCalled()
  },
)

it.each(['primary', 'secondary'] as const)(
  'cancels an in-flight %s probe and releases its timer',
  async (kind) => {
    vi.useFakeTimers()
    const controller = new AbortController()
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url, options) =>
          new Promise((_resolve, reject) => {
            options.signal.addEventListener('abort', () =>
              reject(new DOMException('aborted', 'AbortError')),
            )
          }),
      ),
    )
    const pending =
      kind === 'primary'
        ? probeClashChannel(
            { protocol: 'http', host: 'example.com', port: '9090' } as Backend,
            10000,
            controller.signal,
          )
        : diagnoseConnection('http://example.com/version', controller.signal)
    const assertion = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
    controller.abort()
    await assertion
    expect(vi.getTimerCount()).toBe(0)
  },
)

it.each(Object.values(LANG))('has complete diagnostic messages in %s', async (locale) => {
  i18n.global.locale.value = locale
  for (const key of [
    'diagnosisUnauthorized',
    'diagnosisTimeout',
    'diagnosisBadEndpoint',
    'diagnosisOffline',
    'diagnosisCorsBlocked',
    'diagnosisMixedContent',
    'diagnosisMixedContentOrUnreachable',
    'diagnosisUnreachable',
  ]) {
    expect(i18n.global.te(key, locale)).toBe(true)
    expect(i18n.global.t(key)).not.toBe(key)
  }
  expect(
    await describeProbeFailure({ kind: 'http', message: 'HTTP 404' }, 'http://example.com/version'),
  ).toBe(`HTTP 404 — ${i18n.global.t('diagnosisBadEndpoint')}`)
  expect(await describeProbeFailure({ kind: 'timeout' }, 'http://example.com/version')).toBe(
    i18n.global.t('diagnosisTimeout'),
  )
  expect(await describeConnectionError('explicit server error', 'http://example.com/version')).toBe(
    'explicit server error',
  )
})

it.each(['edit', 'setup'] as const)(
  'uses bounded network diagnostics in the %s form',
  async (mode) => {
    vi.mocked(probeBackend).mockResolvedValue({ ok: false, kind: 'network' })
    const fetchMock = vi.fn().mockResolvedValue({ type: 'opaque' })
    vi.stubGlobal('fetch', fetchMock)
    const root = document.createElement('div')
    root.id = 'app-content'
    document.body.append(root)
    const app = createApp({
      render: () => (mode === 'edit' ? h(EditBackend, { modelValue: true }) : h(SetupPage)),
    })
    app.use(
      createI18n({
        legacy: false,
        locale: 'en',
        messages: { en: {} },
        missingWarn: false,
        fallbackWarn: false,
      }),
    )
    app.mount(root)
    try {
      await nextTick()
      ;[...root.querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.trim() === (mode === 'edit' ? 'save' : 'submit'))!
        .click()
      await vi.waitFor(() =>
        expect(showNotification).toHaveBeenCalledWith({
          content: i18n.global.t('diagnosisCorsBlocked'),
          type: 'alert-error',
        }),
      )
      expect(fetchMock).toHaveBeenCalledWith(
        mode === 'edit' ? 'http://a.example:9090/version' : 'http://127.0.0.1:9090/version',
        expect.objectContaining({ mode: 'no-cors', credentials: 'omit' }),
      )
      expect(fetchMock.mock.calls[0]![1]).not.toHaveProperty('headers')
      expect(addBackend).not.toHaveBeenCalled()
      expect(updateBackend).not.toHaveBeenCalled()
    } finally {
      app.unmount()
      root.remove()
    }
  },
)

it.each([
  ['edit', 'draft'],
  ['edit', 'slot'],
  ['edit', 'closed'],
  ['edit', 'unmounted'],
  ['edit', 'session'],
  ['setup', 'draft'],
  ['setup', 'unmounted'],
  ['setup', 'session'],
] as const)(
  'cancels stale %s diagnostics on %s without notifying or saving',
  async (mode, change) => {
    vi.mocked(probeBackend).mockResolvedValue({ ok: false, kind: 'network' })
    let probeSignal!: AbortSignal
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url, options) =>
          new Promise((_resolve, reject) => {
            probeSignal = options.signal
            probeSignal.addEventListener('abort', () =>
              reject(new DOMException('aborted', 'AbortError')),
            )
          }),
      ),
    )
    const visible = ref(true)
    const root = document.createElement('div')
    root.id = 'app-content'
    document.body.append(root)
    const app = createApp({
      render: () =>
        mode === 'edit' ? h(EditBackend, { modelValue: visible.value }) : h(SetupPage),
    })
    app.use(
      createI18n({
        legacy: false,
        locale: 'en',
        messages: { en: {} },
        missingWarn: false,
        fallbackWarn: false,
      }),
    )
    app.mount(root)
    try {
      await nextTick()
      ;[...root.querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.trim() === (mode === 'edit' ? 'save' : 'submit'))!
        .click()
      await vi.waitFor(() => expect(probeSignal).toBeDefined())
      if (change === 'unmounted') app.unmount()
      else if (change === 'closed') visible.value = false
      else if (change === 'session') activeBackend.value!.password = `session-${mode}`
      else if (change === 'slot') {
        const select = root.querySelector('select')!
        select.value = 'b'
        select.dispatchEvent(new Event('change', { bubbles: true }))
      } else {
        const input = root.querySelector('input')!
        input.value = 'changed.example'
        input.dispatchEvent(new Event('input', { bubbles: true }))
      }
      await vi.waitFor(() => expect(probeSignal.aborted).toBe(true))
      await nextTick()
      await nextTick()
      expect(showNotification).not.toHaveBeenCalled()
      expect(addBackend).not.toHaveBeenCalled()
      expect(updateBackend).not.toHaveBeenCalled()
    } finally {
      if (change !== 'unmounted') app.unmount()
      root.remove()
    }
  },
)

it('diagnoses a browser session manual network error using the API version endpoint', async () => {
  const fetchMock = vi.fn().mockResolvedValue({ type: 'opaque' })
  vi.stubGlobal('fetch', fetchMock)
  const session = captureBackendSession()
  const error = new AxiosError('Network Error', 'ERR_NETWORK', {
    headers: new AxiosHeaders(),
    url: '/cache/dns/flush',
    backendSession: session,
  })
  await notifyRequestErrorForSession(error, session)
  expect(fetchMock).toHaveBeenCalledWith(
    'http://a.example:9090/api/version',
    expect.objectContaining({ mode: 'no-cors', credentials: 'omit' }),
  )
  expect(showNotification).toHaveBeenCalledWith(
    expect.objectContaining({ content: i18n.global.t('diagnosisCorsBlocked') }),
  )
})

it('does not probe external manual errors or errors without a bound backend request', async () => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  const session = captureBackendSession()
  for (const config of [
    { url: 'https://external.example/resource', backendSession: session },
    { url: '//external.example/resource', backendSession: session },
    { url: '/proxies' },
  ]) {
    const error = new AxiosError('Network Error', 'ERR_NETWORK', {
      headers: new AxiosHeaders(),
      ...config,
    })
    await notifyRequestErrorForSession(error, session)
  }
  expect(fetchMock).not.toHaveBeenCalled()
  expect(showNotification).toHaveBeenCalledTimes(3)
})

it('suppresses a stale request even when its caller supplies a fresh session', async () => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  const oldSession = captureBackendSession()
  activeBackend.value!.password = 'fresh-secret'
  const error = new AxiosError('Network Error', 'ERR_NETWORK', {
    headers: new AxiosHeaders(),
    url: '/proxies',
    backendSession: oldSession,
  })
  await notifyRequestErrorForSession(error, captureBackendSession())
  expect(fetchMock).not.toHaveBeenCalled()
  expect(showNotification).not.toHaveBeenCalled()
})

it('aborts session diagnostics when credentials change and suppresses the obsolete result', async () => {
  let signal!: AbortSignal
  vi.stubGlobal(
    'fetch',
    vi.fn(
      (_url, options) =>
        new Promise((_resolve, reject) => {
          signal = options.signal
          signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
        }),
    ),
  )
  const session = captureBackendSession()
  const error = new AxiosError('Network Error', 'ERR_NETWORK', {
    headers: new AxiosHeaders(),
    url: '/proxies',
    backendSession: session,
  })
  const pending = notifyRequestErrorForSession(error, session)
  await vi.waitFor(() => expect(signal).toBeDefined())
  activeBackend.value!.password = 'changed-secret'
  await pending
  expect(signal.aborted).toBe(true)
  expect(showNotification).not.toHaveBeenCalled()
})
