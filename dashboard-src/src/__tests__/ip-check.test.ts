import { getIPFromCloudflareAPI, getIPFromIpipnetAPI, getIPInfo } from '@/api/geoip'
import IPCheck from '@/components/overview/IPCheck.vue'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import {
  getIPs,
  ipForChina,
  ipForGlobal,
  isCheckingIPs,
  resetNetworkChecks,
} from '@/helper/overview'
import { i18n } from '@/i18n'
import { autoIPCheck, IPInfoAPI } from '@/store/settings'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App, type Ref } from 'vue'

vi.mock('@/api/geoip', () => ({
  getIPFromCloudflareAPI: vi.fn(),
  getIPFromIpipnetAPI: vi.fn(),
  getIPInfo: vi.fn(),
}))
vi.mock('@/helper/backendSession', async () => {
  const { ref } = await import('vue')
  return { backendSessionGeneration: ref(0), backendSessionReady: ref(true) }
})
vi.mock('@/i18n', async () => ({
  i18n: (await import('vue-i18n')).createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        getting: 'Querying',
        networkIPQueryFailed: 'Not obtained',
        networkNotTested: 'Not tested',
        networkIPLocationLabel: 'Location',
        networkIPISP: 'ISP',
        networkIPASN: 'ASN',
        networkIPLocationLoading: 'Looking up location',
        networkIPLocationPartial: 'Limited location data',
        networkIPLocationPartialHelp: 'Location unavailable',
        networkInfo: 'Network',
        ipScreenshotTip: 'Privacy',
        networkExitHelp: 'Observed exit',
        networkTestIPs: 'Check IPs',
      },
      zh: { getting: '查询中', networkIPQueryFailed: '未获取' },
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
const sessionGeneration = backendSessionGeneration as Ref<number>
const sessionReady = backendSessionReady as Ref<boolean>
const v4 = '192.0.2.5'
const v6 = '2001:db8:1234:5678:abcd:ef01:2345:6789'
const chinaResult = (ip = '192.0.2.3') => ({ data: { ip, location: ['China', 'Region'] } })
const globalResult = (ip: string, country = 'Hong Kong') => ({
  ip,
  country,
  region: 'Region',
  city: 'City',
  organization: 'Example ISP',
  asn: '64500',
  asnOrganization: 'Example ASN Org',
  latitude: null,
  longitude: null,
})
let app: App | undefined
let root: HTMLDivElement | undefined
beforeEach(() => {
  resetNetworkChecks()
  vi.resetAllMocks()
  sessionGeneration.value = 0
  sessionReady.value = true
  autoIPCheck.value = true
  IPInfoAPI.value = 'fixture' as typeof IPInfoAPI.value
  i18n.global.locale.value = 'en'
  vi.mocked(getIPFromCloudflareAPI).mockResolvedValue({ ip: v4, loc: 'US' })
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue(chinaResult())
  vi.mocked(getIPInfo).mockImplementation(async (ip) => globalResult(ip))
})
afterEach(() => {
  resetNetworkChecks()
  app?.unmount()
  app = undefined
  root?.remove()
  root = undefined
  vi.useRealTimers()
  vi.restoreAllMocks()
})
const mount = () => {
  root = document.createElement('div')
  document.body.append(root)
  app = createApp(IPCheck)
  app.use(i18n)
  app.mount(root)
}
const serviceElement = (service: 'cloudflare' | 'ipip') =>
  root!.querySelector(`[data-ip-service="${service}"]`)!

it('observes each service once and enriches only the observed Cloudflare address without replacing it', async () => {
  vi.mocked(getIPInfo).mockImplementation(async (ip) => ({ ...globalResult(ip), ip: '192.0.2.99' }))
  await getIPs()
  expect(getIPFromCloudflareAPI).toHaveBeenCalledExactlyOnceWith(expect.any(AbortSignal))
  expect(getIPFromIpipnetAPI).toHaveBeenCalledExactlyOnceWith(expect.any(AbortSignal))
  expect(getIPInfo).toHaveBeenCalledExactlyOnceWith(v4, 'fixture', expect.any(AbortSignal))
  expect(ipForGlobal.value.ipWithPrivacy).toEqual(['Hong Kong Region City', v4])
  expect(ipForGlobal.value.detailsWithPrivacy).toEqual({
    location: 'Hong Kong Region City',
    organization: 'Example ISP',
    asn: 'AS64500 Example ASN Org',
  })
  expect(ipForGlobal.value.details).toEqual({
    location: 'Hong Kong ** ** **',
    organization: 'Example **',
    asn: 'AS64500 ** ** **',
  })
  expect(ipForGlobal.value.ip).toEqual(['Hong Kong ** ** **', '192.0.***.***'])
  expect(ipForChina.value.ipWithPrivacy).toEqual(['China Region', '192.0.2.3'])
})

it.each([v4, v6, '::ffff:192.0.2.5'])(
  'renders one address per service and keeps %s and location details out of hidden DOM and titles',
  async (ip) => {
    vi.mocked(getIPFromCloudflareAPI).mockResolvedValue({ ip, loc: 'US' })
    vi.mocked(getIPFromIpipnetAPI).mockResolvedValue(chinaResult(ip))
    mount()
    await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
    for (const service of [ipForChina.value, ipForGlobal.value]) {
      expect(service.state).toBe('ready')
      expect(service.ipWithPrivacy[1]).toBe(ip)
      if (ip === v6) {
        expect(service.ip[1]).toBe('2001:db8:1234:5678:****:****:****:****')
      }
    }
    expect(getIPFromCloudflareAPI).toHaveBeenCalledTimes(1)
    expect(getIPFromIpipnetAPI).toHaveBeenCalledTimes(1)
    expect(root!.querySelectorAll('[data-ip-service]')).toHaveLength(2)
    expect(root!.querySelectorAll('.network-ip-address')).toHaveLength(2)
    expect(root!.querySelectorAll('.network-ip-location')).toHaveLength(2)
    expect(root!.querySelectorAll('[data-ip-family]')).toHaveLength(0)
    expect(root!.textContent).not.toMatch(/IPv4|IPv6|Domestic|International/)
    expect(root!.textContent).toContain('Hong Kong ** ** **')
    expect(root!.innerHTML).not.toContain(ip)
    expect(root!.innerHTML).not.toContain('Example ISP')
    expect(root!.innerHTML).not.toContain('Example ASN Org')
    expect(
      serviceElement('cloudflare').querySelector('.network-ip-isp .network-ip-value')!.textContent,
    ).toBe('Example **')
    expect(
      serviceElement('cloudflare').querySelector('.network-ip-asn .network-ip-value')!.textContent,
    ).toBe('AS64500 ** ** **')
    expect(root!.innerHTML).not.toContain('China Region')
    root!.querySelector<HTMLButtonElement>('button')!.click()
    await nextTick()
    expect(serviceElement('cloudflare').textContent).toContain(ip)
    expect(serviceElement('cloudflare').textContent).toContain('Hong Kong Region City')
    expect(serviceElement('cloudflare').querySelector('.network-ip-isp')!.textContent).toContain(
      'Example ISP',
    )
    expect(serviceElement('cloudflare').querySelector('.network-ip-asn')!.textContent).toContain(
      'AS64500 Example ASN Org',
    )
    expect(serviceElement('ipip').querySelector('.network-ip-details')).toBeNull()
    expect(serviceElement('ipip').textContent).toContain(ip)
    expect(serviceElement('ipip').textContent).toContain('China Region')
    root!.querySelector<HTMLButtonElement>('button')!.click()
    await nextTick()
    expect(root!.innerHTML).not.toContain(ip)
    expect(root!.innerHTML).not.toContain('Example ISP')
    expect(root!.innerHTML).not.toContain('Example ASN Org')
    expect(
      serviceElement('cloudflare').querySelector('.network-ip-isp .network-ip-value')!.textContent,
    ).toBe('Example **')
    expect(
      serviceElement('cloudflare').querySelector('.network-ip-asn .network-ip-value')!.textContent,
    ).toBe('AS64500 ** ** **')
  },
)

it.each([
  { organization: 'Different ISP', asn: 'AS64501', asnOrganization: 'Independent ASN' },
  { organization: 'Solo', asn: '64501', asnOrganization: undefined },
  { organization: '', asn: '64501', asnOrganization: 'Independent ASN' },
  { organization: 'Different ISP', asn: '', asnOrganization: 'Orphaned ASN organization' },
])('renders only independent returned organization and ASN fields: %j', async (info) => {
  vi.mocked(getIPInfo).mockResolvedValue({ ...globalResult(v4), ...info })
  mount()
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
  const hidden = serviceElement('cloudflare')
  expect(hidden.querySelector('.network-ip-isp .network-ip-value')?.textContent ?? '').toBe(
    !info.organization ? '' : info.organization === 'Solo' ? 'Solo' : 'Different **',
  )
  expect(hidden.querySelector('.network-ip-asn .network-ip-value')?.textContent ?? '').toBe(
    !info.asn ? '' : info.asnOrganization ? 'AS64501 ** **' : 'AS64501',
  )
  root!.querySelector<HTMLButtonElement>('button')!.click()
  await nextTick()
  const foreign = serviceElement('cloudflare')
  expect(foreign.querySelector('.network-ip-isp')?.textContent ?? '').toBe(
    info.organization ? `ISP${info.organization}` : '',
  )
  const asnText = foreign.querySelector('.network-ip-asn')?.textContent ?? ''
  expect(asnText).toBe(
    info.asn ? `ASNAS64501${info.asnOrganization ? ` ${info.asnOrganization}` : ''}` : '',
  )
  expect(foreign.querySelector('.network-ip-location')!.textContent).not.toContain('Different ISP')
  expect(serviceElement('ipip').querySelector('.network-ip-details')).toBeNull()
  if (!info.asn) expect(foreign.textContent).not.toContain('Orphaned ASN organization')
  expect(getIPInfo).toHaveBeenCalledTimes(1)
})

it.each(['malformed', 'failed'] as const)(
  'keeps the IPIP result independent when Cloudflare is %s',
  async (scenario) => {
    if (scenario === 'failed')
      vi.mocked(getIPFromCloudflareAPI).mockRejectedValue(new Error('offline'))
    else vi.mocked(getIPFromCloudflareAPI).mockResolvedValue({ ip: '<img src=x>', loc: 'US' })
    await getIPs()
    expect(ipForChina.value.state).toBe('ready')
    expect(ipForGlobal.value.state).toBe('failed')
    expect(ipForGlobal.value.ipWithPrivacy[1]).toBe('')
    expect(getIPInfo).not.toHaveBeenCalled()
  },
)

it('publishes Cloudflare before enrichment and independently of a pending IPIP response', async () => {
  const pending = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  const ipip = deferred<Awaited<ReturnType<typeof getIPFromIpipnetAPI>>>()
  vi.mocked(getIPInfo).mockReturnValue(pending.promise)
  vi.mocked(getIPFromIpipnetAPI).mockReturnValue(ipip.promise)
  const task = getIPs()
  await vi.waitFor(() => expect(ipForGlobal.value.state).toBe('ready'))
  expect(ipForGlobal.value.ipWithPrivacy).toEqual(['US', v4])
  expect(ipForGlobal.value.locationState).toBe('loading')
  expect(ipForGlobal.value.detailsWithPrivacy).toEqual({
    location: 'US',
    organization: '',
    asn: '',
  })
  expect(ipForChina.value.state).toBe('loading')
  pending.resolve(globalResult(v4))
  await vi.waitFor(() => expect(ipForGlobal.value.locationState).toBe('ready'))
  expect(isCheckingIPs.value).toBe(true)
  ipip.resolve(chinaResult())
  await task
})

it.each(['failed', 'incomplete'] as const)(
  'retains the observed address and marks only its location partial when enrichment is %s',
  async (scenario) => {
    if (scenario === 'failed') vi.mocked(getIPInfo).mockRejectedValue(new Error('GeoIP failed'))
    else vi.mocked(getIPInfo).mockResolvedValue({ ...globalResult(v4), asn: '' })
    await getIPs()
    expect(ipForGlobal.value.state).toBe('ready')
    expect(ipForGlobal.value.ipWithPrivacy[1]).toBe(v4)
    expect(ipForGlobal.value.locationState).toBe('partial')
    expect(ipForChina.value.state).toBe('ready')
    if (scenario === 'failed') expect(ipForGlobal.value.ipWithPrivacy[0]).toBe('US')
  },
)

it('shared automatic/manual work survives remount and duplicate clicks without repeating requests', async () => {
  const pending = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  vi.mocked(getIPInfo).mockReturnValue(pending.promise)
  mount()
  await vi.waitFor(() => expect(getIPInfo).toHaveBeenCalledTimes(1))
  app!.unmount()
  app = undefined
  root!.remove()
  mount()
  expect(root!.querySelectorAll<HTMLButtonElement>('button')[1].disabled).toBe(true)
  await getIPs()
  expect(getIPFromCloudflareAPI).toHaveBeenCalledTimes(1)
  expect(getIPFromIpipnetAPI).toHaveBeenCalledTimes(1)
  expect(root!.textContent).toContain('Looking up location')
  pending.resolve(globalResult(v4))
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
  root!.querySelectorAll<HTMLButtonElement>('button')[1].click()
  await vi.waitFor(() => expect(getIPFromCloudflareAPI).toHaveBeenCalledTimes(2))
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
})

it('does not auto-query when disabled, but the manual button starts a check', async () => {
  autoIPCheck.value = false
  mount()
  await nextTick()
  expect(getIPFromCloudflareAPI).not.toHaveBeenCalled()
  expect(root!.textContent).toContain('Not tested')
  root!.querySelectorAll<HTMLButtonElement>('button')[1].click()
  await vi.waitFor(() => expect(ipForGlobal.value.state).toBe('ready'))
})

it.each(['source', 'generation', 'readiness'] as const)(
  'cancels old enrichment on %s changes and old completion cannot clear new busy state',
  async (transition) => {
    const old = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
    const current = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
    vi.mocked(getIPInfo).mockReturnValue(old.promise)
    const oldTask = getIPs()
    await vi.waitFor(() => expect(getIPInfo).toHaveBeenCalledTimes(1))
    const oldSignals = [
      vi.mocked(getIPFromCloudflareAPI).mock.calls[0][0]!,
      vi.mocked(getIPFromIpipnetAPI).mock.calls[0][0]!,
      vi.mocked(getIPInfo).mock.calls[0][2]!,
    ]
    vi.mocked(getIPInfo).mockReturnValue(current.promise)
    if (transition === 'source') IPInfoAPI.value = 'new-source' as typeof IPInfoAPI.value
    else {
      if (transition === 'generation') sessionGeneration.value++
      else sessionReady.value = false
      expect(ipForGlobal.value.state).toBe('idle')
      expect(ipForChina.value.ipWithPrivacy).toEqual([])
      sessionReady.value = true
    }
    expect(oldSignals.every((signal) => signal.aborted)).toBe(true)
    const newTask = transition === 'source' ? undefined : getIPs()
    await vi.waitFor(() => expect(getIPInfo).toHaveBeenCalledTimes(2))
    expect(getIPInfo).toHaveBeenLastCalledWith(
      v4,
      transition === 'source' ? 'new-source' : 'fixture',
      expect.any(AbortSignal),
    )
    old.resolve(globalResult(v4, 'Old'))
    await oldTask
    expect(isCheckingIPs.value).toBe(true)
    expect(ipForGlobal.value.locationState).toBe('loading')
    expect(ipForGlobal.value.detailsWithPrivacy).toEqual({
      location: 'US',
      organization: '',
      asn: '',
    })
    current.resolve(globalResult(v4, 'New'))
    await newTask
    await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
    expect(ipForGlobal.value.ipWithPrivacy).toEqual(['New Region City', v4])
    expect(ipForGlobal.value.detailsWithPrivacy.asn).toBe('AS64500 Example ASN Org')
  },
)

it.each(['source', 'generation'] as const)(
  'late address observations after %s changes cannot overwrite current results or start old enrichment',
  async (transition) => {
    const oldTrace = deferred<Awaited<ReturnType<typeof getIPFromCloudflareAPI>>>()
    const oldIpip = deferred<Awaited<ReturnType<typeof getIPFromIpipnetAPI>>>()
    vi.mocked(getIPFromCloudflareAPI).mockReturnValueOnce(oldTrace.promise)
    vi.mocked(getIPFromIpipnetAPI).mockReturnValueOnce(oldIpip.promise)
    const oldTask = getIPs()
    const signals = [
      vi.mocked(getIPFromCloudflareAPI).mock.calls[0][0]!,
      vi.mocked(getIPFromIpipnetAPI).mock.calls[0][0]!,
    ]
    if (transition === 'source') IPInfoAPI.value = 'new-source' as typeof IPInfoAPI.value
    else sessionGeneration.value++
    expect(signals.every((signal) => signal.aborted)).toBe(true)
    if (transition === 'source') await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
    oldTrace.resolve({ ip: v6, loc: 'Old' })
    oldIpip.resolve(chinaResult(v6))
    await oldTask
    expect(isCheckingIPs.value).toBe(false)
    if (transition === 'source') {
      expect(getIPInfo).toHaveBeenCalledExactlyOnceWith(v4, 'new-source', expect.any(AbortSignal))
      expect(ipForGlobal.value.ipWithPrivacy[1]).toBe(v4)
      expect(ipForChina.value.ipWithPrivacy[1]).toBe('192.0.2.3')
    } else {
      expect(getIPInfo).not.toHaveBeenCalled()
      for (const service of [ipForChina.value, ipForGlobal.value]) {
        expect(service.state).toBe('idle')
        expect(service.ipWithPrivacy).toEqual([])
      }
    }
  },
)

it('malformed IPIP data fails independently and uses the current locale', async () => {
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue({ data: { ip: v4, location: [null] } } as never)
  i18n.global.locale.value = 'zh'
  await getIPs()
  expect(ipForChina.value.state).toBe('failed')
  expect(ipForChina.value.ip[0]).toBe('未获取')
  expect(ipForGlobal.value.state).toBe('ready')
})

it('requests each original service hostname once without literal targets, credentials or fallback', async () => {
  const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
  const fetchMock = vi
    .spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(new Response(`ip=${v4}\nloc=HK\n`))
    .mockResolvedValueOnce(Response.json(chinaResult()))
  expect(await api.getIPFromCloudflareAPI()).toEqual({ ip: v4, loc: 'HK' })
  expect(await api.getIPFromIpipnetAPI()).toEqual(chinaResult())
  expect(fetchMock.mock.calls.map((call) => call[0])).toEqual([
    'https://www.cloudflare.com/cdn-cgi/trace',
    'https://myip.ipip.net/json',
  ])
  for (const call of fetchMock.mock.calls) {
    expect(call[1]).toMatchObject({
      credentials: 'omit',
      referrerPolicy: 'no-referrer',
      cache: 'no-store',
      signal: expect.any(AbortSignal),
    })
  }
})

it.each(['cloudflare', 'ipip'] as const)(
  'forwards cancellation to the %s request',
  async (service) => {
    const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(
      async (_url, options) =>
        new Promise((_resolve, reject) => {
          options!.signal!.addEventListener(
            'abort',
            () => reject(new DOMException('Aborted', 'AbortError')),
            { once: true },
          )
        }),
    )
    const controller = new AbortController()
    const task =
      service === 'cloudflare'
        ? api.getIPFromCloudflareAPI(controller.signal)
        : api.getIPFromIpipnetAPI(controller.signal)
    controller.abort()
    await expect(task).rejects.toThrow('Aborted')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  },
)

it.each(['request', 'body'] as const)(
  'bounds the Cloudflare %s with an eight-second timeout',
  async (phase) => {
    const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
    vi.useFakeTimers()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url, options) => {
      const pending = () =>
        new Promise<string>((_resolve, reject) => {
          options!.signal!.addEventListener(
            'abort',
            () => reject(new DOMException('Aborted', 'AbortError')),
            { once: true },
          )
        })
      if (phase === 'request') return (await pending()) as never
      return { ok: true, text: pending } as Response
    })
    const task = api.getIPFromCloudflareAPI()
    const rejected = expect(task).rejects.toThrow('Aborted')
    await vi.advanceTimersByTimeAsync(8000)
    await rejected
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][1]!.signal!.aborted).toBe(true)
    expect(vi.getTimerCount()).toBe(0)
  },
)

it('rejects HTTP errors without retry or fallback', async () => {
  const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
  const fetchMock = vi
    .spyOn(globalThis, 'fetch')
    .mockResolvedValue(new Response('', { status: 503 }))
  await expect(api.getIPFromCloudflareAPI()).rejects.toThrow('503')
  expect(fetchMock).toHaveBeenCalledTimes(1)
})

it.each([
  {
    apiName: 'IPSB' as const,
    data: { organization: 'ISP owner', asn: 64500, asn_organization: 'ASN owner' },
  },
  {
    apiName: 'IPWHOIS' as const,
    data: { success: true, connection: { isp: 'ISP owner', asn: 64500, org: 'ASN owner' } },
  },
])('keeps ISP and ASN organization distinct from $apiName metadata', async ({ apiName, data }) => {
  const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
  const { IP_INFO_API } = await import('@/constant')
  const fetchMock = vi
    .spyOn(globalThis, 'fetch')
    .mockResolvedValue(Response.json({ ip: v4, ...data }))
  expect(await api.getIPInfo(v4, IP_INFO_API[apiName])).toMatchObject({
    ip: v4,
    organization: 'ISP owner',
    asn: '64500',
    asnOrganization: 'ASN owner',
  })
  expect(fetchMock).toHaveBeenCalledTimes(1)
})

it('decodes flat, nested and nullable ipapi.is metadata for an explicit IP', async () => {
  const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
  const { IP_INFO_API } = await import('@/constant')
  const fetchMock = vi
    .spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(
      Response.json({
        ip: '1.1.1.1',
        country: 'Australia',
        company: 'APNIC',
        asn: 'AS13335 Cloudflare, Inc.',
      }),
    )
    .mockResolvedValueOnce(
      Response.json({
        ip: '1.1.1.1',
        location: { country: 'Australia', city: 'Brisbane' },
        company: { name: 'Cloudflare' },
        asn: { asn: 13335, org: 'Cloudflare' },
      }),
    )
    .mockResolvedValueOnce(Response.json({ ip: '1.1.1.1', company: null, asn: null }))
  expect(await api.getIPInfo('1.1.1.1', IP_INFO_API.IPAPI)).toMatchObject({
    country: 'Australia',
    organization: 'APNIC',
    asn: '13335',
    asnOrganization: 'Cloudflare, Inc.',
  })
  expect(await api.getIPInfo('1.1.1.1', IP_INFO_API.IPAPI)).toMatchObject({
    country: 'Australia',
    city: 'Brisbane',
    organization: 'Cloudflare',
    asn: '13335',
    asnOrganization: 'Cloudflare',
  })
  expect(await api.getIPInfo('1.1.1.1', IP_INFO_API.IPAPI)).toMatchObject({
    organization: '',
    asn: '',
  })
  expect(fetchMock).toHaveBeenCalledWith('https://api.ipapi.is/?q=1.1.1.1', expect.any(Object))
})
