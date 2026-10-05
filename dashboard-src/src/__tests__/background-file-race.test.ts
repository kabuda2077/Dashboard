import { expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, ref } from 'vue'
import { createI18n } from 'vue-i18n'
vi.mock('@/store/settings', () => ({
  customBackgroundURL: ref(''),
  autoTheme: ref(false),
  blurIntensity: ref(0),
  dashboardTransparent: ref(90),
  defaultTheme: ref('light'),
  theme: ref('light'),
}))
vi.mock('@/helper/requestError', () => ({ notifyRequestError: vi.fn() }))
vi.mock('@/components/settings/SettingItem.vue', () => ({
  default: defineComponent({
    setup:
      (_, { slots }) =>
      () =>
        h('div', slots.default?.()),
  }),
}))
vi.mock('@/components/common/TextInput.vue', () => ({ default: { render: () => null } }))
it('the real upload component cancels old decoding and native flush waits for the selected image', async () => {
  vi.resetModules()
  localStorage.clear()
  const requests = new Map<string, { onload: (() => void) | null; onerror: (() => void) | null }>()
  vi.stubGlobal(
    'Image',
    class {
      onload = null
      onerror = null
      set src(value: string) {
        requests.set(value, this)
      }
    },
  )
  vi.stubGlobal(
    'FileReader',
    class {
      result = ''
      onload?: () => void
      onerror?: () => void
      abort() {}
      readAsDataURL(file: File) {
        this.result = file.name
        queueMicrotask(() => this.onload?.())
      }
    },
  )
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    drawImage: () => {},
    getImageData: () => ({ data: new Uint8ClampedArray([255, 255, 255, 255]) }),
  } as never)
  const { default: Background } =
    await import('@/components/settings/general/BackgroundSettings.vue')
  const { getBase64FromIndexedDB } = await import('@/helper/indexeddb')
  const { flushLocalPersistence } = await import('@/helper/persistenceBarrier')
  const root = document.createElement('div')
  document.body.append(root)
  const app = createApp(Background)
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
    const input = root.querySelector<HTMLInputElement>('input[type=file]')!
    const select = (name: string) => {
      Object.defineProperty(input, 'files', {
        configurable: true,
        value: [new File(['image'], name, { type: 'image/png' })],
      })
      input.dispatchEvent(new Event('change', { bubbles: true }))
    }
    select('A')
    await vi.waitFor(() => expect(requests.has('A')).toBe(true))
    const oldCallback = requests.get('A')!.onload
    select('B')
    await vi.waitFor(() => expect(requests.has('B')).toBe(true))
    let flushed = false
    const flush = flushLocalPersistence().then(() => {
      flushed = true
    })
    await Promise.resolve()
    expect(flushed).toBe(false)
    requests.get('B')!.onload?.()
    await flush
    oldCallback?.()
    expect(await getBase64FromIndexedDB()).toBe('B')
    expect(flushed).toBe(true)
  } finally {
    app.unmount()
    root.remove()
    window.dispatchEvent(new Event('pagehide'))
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  }
})

it('an undecodable file leaves the previous committed image and preference intact', async () => {
  vi.resetModules()
  localStorage.clear()
  vi.stubGlobal('Image', class {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    set src(_value: string) { queueMicrotask(() => this.onerror?.()) }
  })
  vi.stubGlobal('FileReader', class {
    result = 'broken-image'
    onload?: () => void
    abort() {}
    readAsDataURL() { queueMicrotask(() => this.onload?.()) }
  })
  const db = await import('@/helper/indexeddb')
  await db.saveBase64ToIndexedDB('original')
  const { default: Background } = await import('@/components/settings/general/BackgroundSettings.vue')
  const { customBackgroundURL } = await import('@/store/settings')
  const previousUrl = customBackgroundURL.value
  const { notifyRequestError } = await import('@/helper/requestError')
  vi.mocked(notifyRequestError).mockClear()
  const root = document.createElement('div'); document.body.append(root)
  const app = createApp(Background)
  app.use(createI18n({ legacy: false, locale: 'en', messages: { en: {} }, missingWarn: false, fallbackWarn: false }))
  app.mount(root)
  try {
    const input = root.querySelector<HTMLInputElement>('input[type=file]')!
    Object.defineProperty(input, 'files', { configurable: true, value: [new File(['broken'], 'broken.png', { type: 'image/png' })] })
    input.dispatchEvent(new Event('change', { bubbles: true }))
    await vi.waitFor(() => expect(notifyRequestError).toHaveBeenCalled())
    expect(await db.getBase64FromIndexedDB()).toBe('original')
    expect(customBackgroundURL.value).toBe(previousUrl)
  } finally {
    app.unmount(); root.remove(); window.dispatchEvent(new Event('pagehide')); vi.restoreAllMocks(); vi.unstubAllGlobals()
  }
})
