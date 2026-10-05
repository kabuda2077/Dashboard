import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, ref, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import { getIPFromIpipnetAPI, getIPInfo } from '@/api/geoip'
import { ipForChina, ipForGlobal } from '@/composables/overview'
import IPCheck from '@/components/overview/IPCheck.vue'

vi.mock('@/api/geoip', () => ({ getIPFromIpipnetAPI: vi.fn(), getIPInfo: vi.fn() }))
vi.mock('@/composables/overview', () => ({
  ipForChina: ref({ ip: [], ipWithPrivacy: [] }),
  ipForGlobal: ref({ ip: [], ipWithPrivacy: [] }),
}))
vi.mock('@/helper/tooltip', () => ({ useTooltip: () => ({ showTip: vi.fn() }) }))
vi.mock('@/store/settings', () => ({ autoIPCheck: ref(true), IPInfoAPI: ref('fixture') }))

const deferred = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}
let app: App | undefined
let root: HTMLDivElement
beforeEach(() => {
  vi.resetAllMocks()
  ipForChina.value = { ip: [], ipWithPrivacy: [] }
  ipForGlobal.value = { ip: [], ipWithPrivacy: [] }
})
afterEach(() => { app?.unmount(); app = undefined; root?.remove() })
const mount = () => {
  root = document.createElement('div')
  document.body.append(root)
  app = createApp(IPCheck)
  app.use(createI18n({ legacy: false, locale: 'en', messages: { en: {
    getting: 'Querying', testFailed: 'Failed', networkInfo: 'Network', ipScreenshotTip: 'Privacy',
  } } }))
  app.mount(root)
}
const globalResult = (ip: string, country = 'New') => ({ ip, country, organization: 'ISP' }) as Awaited<ReturnType<typeof getIPInfo>>
const chinaResult = (ip: string) => ({ data: { ip, location: ['China', 'Region'] } }) as Awaited<ReturnType<typeof getIPFromIpipnetAPI>>

it('late responses cannot overwrite a newer query; IPv6 is masked until privacy is explicitly revealed', async () => {
  const oldGlobal = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  const oldChina = deferred<Awaited<ReturnType<typeof getIPFromIpipnetAPI>>>()
  vi.mocked(getIPInfo).mockReturnValueOnce(oldGlobal.promise).mockResolvedValueOnce(globalResult('2001:db8::1234'))
  vi.mocked(getIPFromIpipnetAPI).mockReturnValueOnce(oldChina.promise).mockResolvedValueOnce(chinaResult('192.0.2.3'))
  mount()
  root.querySelectorAll<HTMLButtonElement>('button')[1].click()
  await vi.waitFor(() => expect(root.textContent).toContain('New ISP'))
  expect(root.textContent).toContain('2001:db8:****:****')
  expect(root.textContent).not.toContain('2001:db8::1234')
  oldGlobal.resolve(globalResult('192.0.2.99', 'Old'))
  oldChina.resolve(chinaResult('192.0.2.99'))
  await nextTick(); await nextTick()
  expect(root.textContent).not.toContain('Old')
  root.querySelectorAll<HTMLButtonElement>('button')[0].click()
  await nextTick()
  expect(root.textContent).toContain('2001:db8::1234')
  expect(root.textContent).toContain('192.0.2.3')
  expect(root.textContent).not.toContain('192.0.2.99')
})
it('malformed responses become failures without leaving invalid data in the view', async () => {
  vi.mocked(getIPInfo).mockResolvedValue(globalResult('<img src=x onerror=alert(1)>'))
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue({ data: { ip: '192.0.2.1', location: null } } as never)
  mount()
  await vi.waitFor(() => expect(ipForGlobal.value.ip[0]).toBe('Failed'))
  expect(ipForChina.value.ip[0]).toBe('Failed')
  expect(root.querySelector('img')).toBeNull()
})
it('a response after unmount cannot mutate shared IP state', async () => {
  const pending = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  vi.mocked(getIPInfo).mockReturnValue(pending.promise)
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue(chinaResult('192.0.2.3'))
  mount()
  await nextTick()
  app!.unmount(); app = undefined
  const before = JSON.stringify(ipForGlobal.value)
  pending.resolve(globalResult('192.0.2.5'))
  await nextTick(); await nextTick()
  expect(JSON.stringify(ipForGlobal.value)).toBe(before)
})
