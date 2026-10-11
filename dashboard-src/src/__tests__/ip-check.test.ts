import { getIPFromIpipnetAPI, getIPInfo } from '@/api/geoip'
import IPCheck from '@/components/overview/IPCheck.vue'
import { getIPs, ipForChina, ipForGlobal, isCheckingIPs } from '@/composables/overview'
import { i18n } from '@/i18n'
import { IPInfoAPI } from '@/store/settings'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'

vi.mock('@/api/geoip', () => ({ getIPFromIpipnetAPI: vi.fn(), getIPInfo: vi.fn() }))
vi.mock('@/i18n', async () => ({
  i18n: (await import('vue-i18n')).createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        getting: 'Querying',
        testFailed: 'Failed',
        networkInfo: 'Network',
        ipScreenshotTip: 'Privacy',
      },
      zh: { getting: '查询中', testFailed: '失败' },
    },
  }),
}))
vi.mock('@/helper/tooltip', () => ({ useTooltip: () => ({ showTip: vi.fn() }) }))
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return { autoIPCheck: ref(true), IPInfoAPI: ref('fixture') }
})

const deferred = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
let app: App | undefined
let root: HTMLDivElement
beforeEach(() => {
  vi.resetAllMocks()
  ipForChina.value = { ip: [], ipWithPrivacy: [] }
  ipForGlobal.value = { ip: [], ipWithPrivacy: [] }
  IPInfoAPI.value = 'fixture' as typeof IPInfoAPI.value
  i18n.global.locale.value = 'en'
})
afterEach(() => {
  app?.unmount()
  app = undefined
  root?.remove()
})
const mount = () => {
  root = document.createElement('div')
  document.body.append(root)
  app = createApp(IPCheck)
  app.use(i18n)
  app.mount(root)
}
const globalResult = (ip: string, country = 'New') =>
  ({ ip, country, organization: 'ISP' }) as Awaited<ReturnType<typeof getIPInfo>>
const chinaResult = (ip: string) =>
  ({ data: { ip, location: ['China', 'Region'] } }) as Awaited<
    ReturnType<typeof getIPFromIpipnetAPI>
  >

it('late responses cannot overwrite a newer query; IPv6 is masked until privacy is explicitly revealed', async () => {
  const oldGlobal = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  const oldChina = deferred<Awaited<ReturnType<typeof getIPFromIpipnetAPI>>>()
  vi.mocked(getIPInfo)
    .mockReturnValueOnce(oldGlobal.promise)
    .mockResolvedValueOnce(globalResult('2001:db8::1234'))
  vi.mocked(getIPFromIpipnetAPI)
    .mockReturnValueOnce(oldChina.promise)
    .mockResolvedValueOnce(chinaResult('192.0.2.3'))
  mount()
  IPInfoAPI.value = 'new-source' as typeof IPInfoAPI.value
  await vi.waitFor(() => expect(root.textContent).toContain('New ISP'))
  expect(root.textContent).toContain('2001:db8:****:****')
  expect(root.textContent).not.toContain('2001:db8::1234')
  oldGlobal.resolve(globalResult('192.0.2.99', 'Old'))
  oldChina.resolve(chinaResult('192.0.2.99'))
  await nextTick()
  await nextTick()
  expect(root.textContent).not.toContain('Old')
  root.querySelectorAll<HTMLButtonElement>('button')[0].click()
  await nextTick()
  expect(root.textContent).toContain('2001:db8::1234')
  expect(root.textContent).toContain('192.0.2.3')
  expect(root.textContent).not.toContain('192.0.2.99')
})
it('malformed responses become failures without leaving invalid data in the view', async () => {
  vi.mocked(getIPInfo).mockResolvedValue(globalResult('<img src=x onerror=alert(1)>'))
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue({
    data: { ip: '192.0.2.1', location: null },
  } as never)
  mount()
  await vi.waitFor(() => expect(ipForGlobal.value.ip[0]).toBe('Failed'))
  expect(ipForChina.value.ip[0]).toBe('Failed')
  expect(root.querySelector('img')).toBeNull()
})
it('continues after unmount and exposes shared busy state on remount without duplicate requests', async () => {
  const pending = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  vi.mocked(getIPInfo).mockReturnValue(pending.promise)
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue(chinaResult('192.0.2.3'))
  mount()
  await nextTick()
  app!.unmount()
  app = undefined
  root.remove()
  mount()
  expect(root.querySelectorAll<HTMLButtonElement>('button')[1].disabled).toBe(true)
  await getIPs()
  root.querySelectorAll<HTMLButtonElement>('button')[1].click()
  expect(getIPInfo).toHaveBeenCalledTimes(1)
  pending.resolve(globalResult('192.0.2.5'))
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
  expect(ipForGlobal.value.ipWithPrivacy[1]).toBe('192.0.2.5')
  expect(root.textContent).toContain('New ISP')
  expect(root.textContent).not.toContain('Querying')
})

it('source changes while absent supersede old replies and old completion cannot clear the new busy state', async () => {
  const oldGlobal = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  const newGlobal = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  vi.mocked(getIPInfo).mockReturnValueOnce(oldGlobal.promise).mockReturnValueOnce(newGlobal.promise)
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue(chinaResult('192.0.2.3'))
  mount()
  app!.unmount()
  app = undefined
  root.remove()
  IPInfoAPI.value = 'changed-away' as typeof IPInfoAPI.value
  expect(getIPInfo).toHaveBeenCalledTimes(2)
  oldGlobal.resolve(globalResult('192.0.2.99', 'Old'))
  await nextTick()
  await nextTick()
  await nextTick()
  expect(isCheckingIPs.value).toBe(true)
  expect(ipForGlobal.value.ip[0]).toBe('Querying')
  newGlobal.resolve(globalResult('192.0.2.5'))
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
  mount()
  expect(root.textContent).toContain('New ISP')
  expect(getIPInfo).toHaveBeenCalledTimes(2)
})

it('uses the active locale for query and failure text, including failures while absent', async () => {
  const pending = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  vi.mocked(getIPInfo).mockReturnValue(pending.promise)
  vi.mocked(getIPFromIpipnetAPI).mockRejectedValue(new Error('offline'))
  i18n.global.locale.value = 'zh'
  mount()
  expect(ipForGlobal.value.ip[0]).toBe('查询中')
  app!.unmount()
  app = undefined
  pending.resolve(globalResult('invalid'))
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
  expect(ipForGlobal.value.ip[0]).toBe('失败')
  expect(ipForChina.value.ip[0]).toBe('失败')
})
