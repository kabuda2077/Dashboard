import { getIPFromIpipnetAPI, getIPInfo } from '@/api/geoip'
import IPCheck from '@/components/overview/IPCheck.vue'
import { IP_INFO_API } from '@/constant'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import { getIPs, ipForChina, ipForGlobal, isCheckingIPs, resetIPChecks } from '@/helper/overview'
import { i18n } from '@/i18n'
import { autoIPCheck, IPInfoAPI } from '@/store/settings'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App, type Ref } from 'vue'

vi.mock('@/api/geoip', () => ({
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
        networkIPLocationPartial: 'Limited location data',
        networkIPLocationPartialHelp: 'Location unavailable',
        networkInfo: 'Network',
        networkDomestic: 'Domestic',
        networkInternational: 'International',
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
  return { autoIPCheck: ref(true), IPInfoAPI: ref('ip.sb') }
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
  resetIPChecks()
  vi.resetAllMocks()
  sessionGeneration.value = 0
  sessionReady.value = true
  autoIPCheck.value = true
  IPInfoAPI.value = IP_INFO_API.IPSB
  i18n.global.locale.value = 'en'
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue(chinaResult())
  vi.mocked(getIPInfo).mockResolvedValue(globalResult(v4))
})
afterEach(() => {
  resetIPChecks()
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
const serviceElement = (service: 'global' | 'ipip') =>
  root!.querySelector(`[data-ip-service="${service}"]`)!

it('observes the global exit and details with one selected-source self-query alongside IPIP', async () => {
  await getIPs()
  expect(getIPFromIpipnetAPI).toHaveBeenCalledExactlyOnceWith(expect.any(AbortSignal))
  expect(getIPInfo).toHaveBeenCalledExactlyOnceWith('', IPInfoAPI.value, expect.any(AbortSignal))
  expect(ipForGlobal.value.locationState).toBe('ready')
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
    vi.mocked(getIPInfo).mockResolvedValue(globalResult(ip))
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
    expect(getIPInfo).toHaveBeenCalledExactlyOnceWith('', IPInfoAPI.value, expect.any(AbortSignal))
    expect(getIPFromIpipnetAPI).toHaveBeenCalledTimes(1)
    expect(root!.querySelectorAll('[data-ip-service]')).toHaveLength(2)
    expect(root!.querySelectorAll('.network-ip-address')).toHaveLength(2)
    expect(root!.querySelectorAll('.network-ip-location')).toHaveLength(2)
    expect(root!.querySelectorAll('[data-ip-family]')).toHaveLength(0)
    expect(root!.textContent).not.toMatch(/IPv4|IPv6/)
    expect(serviceElement('ipip').querySelector('.network-tile-badge')!.textContent).toContain(
      'Domestic',
    )
    expect(serviceElement('global').querySelector('.network-tile-badge')!.textContent).toContain(
      'International',
    )
    expect(serviceElement('global').querySelector('.network-ip-provider-name')!.textContent).toBe(
      IPInfoAPI.value,
    )
    expect(
      [...serviceElement('global').querySelectorAll('.network-ip-detail .network-ip-label')].map(
        (label) => label.textContent,
      ),
    ).toEqual(['Location', 'ASN', 'ISP'])
    expect(
      [...serviceElement('ipip').querySelectorAll('.network-ip-detail .network-ip-label')].map(
        (label) => label.textContent,
      ),
    ).toEqual(['Location'])
    expect(serviceElement('ipip').querySelector('.network-ip-asn')).toBeNull()
    const location = serviceElement('global').querySelector(
      '.network-ip-location .network-ip-value',
    )!
    const isp = serviceElement('global').querySelector('.network-ip-isp .network-ip-value')!
    const asn = serviceElement('global').querySelector('.network-ip-asn .network-ip-value')!
    const domesticLocation = serviceElement('ipip').querySelector(
      '.network-ip-location .network-ip-value',
    )!
    expect(location.getAttribute('title')).toBe('Hong Kong ** ** **')
    expect(isp.getAttribute('title')).toBe('Example **')
    expect(asn.getAttribute('title')).toBe('AS64500 ** ** **')
    expect(domesticLocation.getAttribute('title')).toBe('China ** ** **')
    expect(root!.textContent).toContain('Hong Kong ** ** **')
    expect(root!.innerHTML).not.toContain(ip)
    expect(root!.innerHTML).not.toContain('Example ISP')
    expect(root!.innerHTML).not.toContain('Example ASN Org')
    expect(
      serviceElement('global').querySelector('.network-ip-isp .network-ip-value')!.textContent,
    ).toBe('Example **')
    expect(asn.textContent).toBe('AS64500')
    expect(root!.innerHTML).not.toContain('Hong Kong Region City')
    expect(root!.innerHTML).not.toContain('China Region')
    root!.querySelector<HTMLButtonElement>('button')!.click()
    await nextTick()
    expect(serviceElement('global').textContent).toContain(ip)
    expect(serviceElement('global').textContent).toContain('Hong Kong Region City')
    expect(serviceElement('global').querySelector('.network-ip-isp')!.textContent).toContain(
      'Example ISP',
    )
    expect(asn.textContent).toBe('AS64500')
    expect(asn.getAttribute('title')).toBe('AS64500 Example ASN Org')
    expect(location.getAttribute('title')).toBe('Hong Kong Region City')
    expect(isp.getAttribute('title')).toBe('Example ISP')
    expect(domesticLocation.getAttribute('title')).toBe('China Region')
    expect(serviceElement('ipip').querySelector('.network-ip-details')).toBeNull()
    expect(serviceElement('ipip').textContent).toContain(ip)
    expect(serviceElement('ipip').textContent).toContain('China Region')
    root!.querySelector<HTMLButtonElement>('button')!.click()
    await nextTick()
    expect(root!.innerHTML).not.toContain(ip)
    expect(root!.innerHTML).not.toContain('Example ISP')
    expect(root!.innerHTML).not.toContain('Example ASN Org')
    expect(
      serviceElement('global').querySelector('.network-ip-isp .network-ip-value')!.textContent,
    ).toBe('Example **')
    expect(asn.textContent).toBe('AS64500')
    expect(asn.getAttribute('title')).toBe('AS64500 ** ** **')
    expect(location.getAttribute('title')).toBe('Hong Kong ** ** **')
    expect(isp.getAttribute('title')).toBe('Example **')
    expect(domesticLocation.getAttribute('title')).toBe('China ** ** **')
    expect(root!.innerHTML).not.toContain('Hong Kong Region City')
    expect(root!.innerHTML).not.toContain('China Region')
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
  const hidden = serviceElement('global')
  expect(hidden.querySelector('.network-ip-isp .network-ip-value')?.textContent ?? '').toBe(
    !info.organization ? '' : info.organization === 'Solo' ? 'Solo' : 'Different **',
  )
  expect(ipForGlobal.value.detailsWithPrivacy.organization).toBe(info.organization)
  expect(ipForGlobal.value.detailsWithPrivacy.asn).toBe(
    info.asn ? `AS64501${info.asnOrganization ? ` ${info.asnOrganization}` : ''}` : '',
  )
  expect(hidden.querySelector('.network-ip-asn .network-ip-value')?.textContent ?? '').toBe(
    info.asn ? 'AS64501' : '',
  )
  expect(
    hidden.querySelector('.network-ip-asn .network-ip-value')?.getAttribute('title') ?? '',
  ).toBe(!info.asn ? '' : info.asnOrganization ? 'AS64501 ** **' : 'AS64501')
  if (info.organization === 'Different ISP')
    expect(hidden.innerHTML).not.toContain(info.organization)
  if (info.asnOrganization) expect(hidden.innerHTML).not.toContain(info.asnOrganization)
  root!.querySelector<HTMLButtonElement>('button')!.click()
  await nextTick()
  const foreign = serviceElement('global')
  expect(foreign.querySelector('.network-ip-isp')?.textContent ?? '').toBe(
    info.organization ? `ISP${info.organization}` : '',
  )
  expect(
    foreign.querySelector('.network-ip-isp .network-ip-value')?.getAttribute('title') ?? '',
  ).toBe(info.organization)
  expect(foreign.querySelector('.network-ip-asn')?.textContent ?? '').toBe(
    info.asn ? 'ASNAS64501' : '',
  )
  expect(
    foreign.querySelector('.network-ip-asn .network-ip-value')?.getAttribute('title') ?? '',
  ).toBe(info.asn ? `AS64501${info.asnOrganization ? ` ${info.asnOrganization}` : ''}` : '')
  expect(foreign.querySelector('.network-ip-location')!.textContent).not.toContain('Different ISP')
  expect(serviceElement('ipip').querySelector('.network-ip-details')).toBeNull()
  if (!info.asn) expect(foreign.innerHTML).not.toContain('Orphaned ASN organization')
  expect(getIPInfo).toHaveBeenCalledTimes(1)
})

it.each(['malformed', 'failed'] as const)(
  'keeps the IPIP result independent when the selected global query is %s',
  async (scenario) => {
    if (scenario === 'failed') vi.mocked(getIPInfo).mockRejectedValue(new Error('offline'))
    else vi.mocked(getIPInfo).mockResolvedValue(globalResult('<img src=x>'))
    mount()
    await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
    expect(ipForChina.value.state).toBe('ready')
    expect(ipForGlobal.value.state).toBe('failed')
    expect(ipForGlobal.value.ipWithPrivacy[1]).toBe('')
    expect(ipForGlobal.value.detailsWithPrivacy).toEqual({
      location: '',
      organization: '',
      asn: '',
    })
    expect(serviceElement('global').textContent).toContain('Not obtained')
    expect(serviceElement('global').querySelector('.network-ip-location')).toBeNull()
    expect(root!.innerHTML).not.toContain('<img')
    expect(getIPInfo).toHaveBeenCalledExactlyOnceWith('', IPInfoAPI.value, expect.any(AbortSignal))
  },
)

it('starts both queries in parallel and publishes global exit and details while IPIP is pending', async () => {
  const pending = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
  const ipip = deferred<Awaited<ReturnType<typeof getIPFromIpipnetAPI>>>()
  vi.mocked(getIPInfo).mockReturnValue(pending.promise)
  vi.mocked(getIPFromIpipnetAPI).mockReturnValue(ipip.promise)
  const task = getIPs()
  expect(getIPInfo).toHaveBeenCalledExactlyOnceWith('', IPInfoAPI.value, expect.any(AbortSignal))
  expect(getIPFromIpipnetAPI).toHaveBeenCalledExactlyOnceWith(expect.any(AbortSignal))
  expect(ipForGlobal.value.state).toBe('loading')
  expect(ipForGlobal.value.locationState).toBe('idle')
  pending.resolve(globalResult(v4))
  await vi.waitFor(() => expect(ipForGlobal.value.state).toBe('ready'))
  expect(ipForGlobal.value.ipWithPrivacy).toEqual(['Hong Kong Region City', v4])
  expect(ipForGlobal.value.locationState).toBe('ready')
  expect(ipForGlobal.value.detailsWithPrivacy).toEqual({
    location: 'Hong Kong Region City',
    organization: 'Example ISP',
    asn: 'AS64500 Example ASN Org',
  })
  expect(ipForChina.value.state).toBe('loading')
  expect(isCheckingIPs.value).toBe(true)
  ipip.resolve(chinaResult())
  await task
  expect(getIPInfo).toHaveBeenCalledTimes(1)
})

it.each([
  { asn: '' },
  { country: '', region: '', city: '', organization: '', asn: '', asnOrganization: undefined },
])(
  'keeps a valid address ready with partial details when fields are absent: %j',
  async (fields) => {
    vi.mocked(getIPInfo).mockResolvedValue({ ...globalResult(v4), ...fields })
    mount()
    await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
    expect(ipForGlobal.value.state).toBe('ready')
    expect(ipForGlobal.value.ipWithPrivacy[1]).toBe(v4)
    expect(ipForGlobal.value.locationState).toBe('partial')
    expect(ipForChina.value.state).toBe('ready')
    expect(serviceElement('global').textContent).toContain('Limited location data')
    expect(serviceElement('global').querySelector('.network-ip-asn')).toBeNull()
    root!.querySelector<HTMLButtonElement>('button')!.click()
    await nextTick()
    expect(serviceElement('global').querySelector('.network-ip-address')!.textContent).toContain(v4)
    expect(getIPInfo).toHaveBeenCalledTimes(1)
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
  expect(getIPInfo).toHaveBeenCalledTimes(1)
  expect(getIPFromIpipnetAPI).toHaveBeenCalledTimes(1)
  expect(serviceElement('global').textContent).toContain('Querying')
  expect(serviceElement('global').querySelector('.network-ip-location')).toBeNull()
  pending.resolve(globalResult(v4))
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
  root!.querySelectorAll<HTMLButtonElement>('button')[1].click()
  await vi.waitFor(() => expect(getIPInfo).toHaveBeenCalledTimes(2))
  await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
})

it('does not auto-query when disabled, but the manual button starts a check', async () => {
  autoIPCheck.value = false
  mount()
  await nextTick()
  expect(getIPInfo).not.toHaveBeenCalled()
  expect(getIPFromIpipnetAPI).not.toHaveBeenCalled()
  expect(root!.textContent).toContain('Not tested')
  root!.querySelectorAll<HTMLButtonElement>('button')[1].click()
  await vi.waitFor(() => expect(ipForGlobal.value.state).toBe('ready'))
})

it.each(['source', 'generation', 'readiness'] as const)(
  'cancels old queries on %s changes and old completion cannot clear new busy state',
  async (transition) => {
    const old = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
    const current = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
    vi.mocked(getIPInfo).mockReturnValue(old.promise)
    const oldTask = getIPs()
    mount()
    expect(serviceElement('global').querySelector('.network-ip-provider-name')!.textContent).toBe(
      IP_INFO_API.IPSB,
    )
    await vi.waitFor(() => expect(getIPInfo).toHaveBeenCalledTimes(1))
    const oldSignals = [
      vi.mocked(getIPFromIpipnetAPI).mock.calls[0][0]!,
      vi.mocked(getIPInfo).mock.calls[0][2]!,
    ]
    vi.mocked(getIPInfo).mockReturnValue(current.promise)
    if (transition === 'source') IPInfoAPI.value = IP_INFO_API.IPWHOIS
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
    await nextTick()
    expect(serviceElement('global').querySelector('.network-ip-provider-name')!.textContent).toBe(
      IPInfoAPI.value,
    )
    expect(getIPFromIpipnetAPI).toHaveBeenCalledTimes(2)
    expect(getIPInfo).toHaveBeenLastCalledWith(
      '',
      transition === 'source' ? IP_INFO_API.IPWHOIS : IP_INFO_API.IPSB,
      expect.any(AbortSignal),
    )
    old.resolve(globalResult(v4, 'Old'))
    await oldTask
    expect(isCheckingIPs.value).toBe(true)
    expect(ipForGlobal.value.state).toBe('loading')
    expect(ipForGlobal.value.locationState).toBe('idle')
    expect(ipForGlobal.value.ipWithPrivacy[1]).toBe('')
    expect(ipForGlobal.value.detailsWithPrivacy).toEqual({
      location: '',
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
  'ignores late replies after unmount and a %s change',
  async (transition) => {
    const oldGlobal = deferred<Awaited<ReturnType<typeof getIPInfo>>>()
    const oldIpip = deferred<Awaited<ReturnType<typeof getIPFromIpipnetAPI>>>()
    vi.mocked(getIPInfo).mockReturnValueOnce(oldGlobal.promise)
    vi.mocked(getIPFromIpipnetAPI).mockReturnValueOnce(oldIpip.promise)
    const oldTask = getIPs()
    mount()
    app!.unmount()
    app = undefined
    root!.remove()
    root = undefined
    const signals = [
      vi.mocked(getIPInfo).mock.calls[0][2]!,
      vi.mocked(getIPFromIpipnetAPI).mock.calls[0][0]!,
    ]
    if (transition === 'source') IPInfoAPI.value = IP_INFO_API.IPWHOIS
    else sessionGeneration.value++
    expect(signals.every((signal) => signal.aborted)).toBe(true)
    if (transition === 'source') await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
    oldGlobal.resolve(globalResult(v6, 'Old'))
    oldIpip.resolve(chinaResult(v6))
    await oldTask
    expect(isCheckingIPs.value).toBe(false)
    if (transition === 'source') {
      expect(getIPInfo).toHaveBeenCalledTimes(2)
      expect(getIPInfo).toHaveBeenLastCalledWith('', IP_INFO_API.IPWHOIS, expect.any(AbortSignal))
      expect(getIPFromIpipnetAPI).toHaveBeenCalledTimes(2)
      expect(ipForGlobal.value.ipWithPrivacy).toEqual(['Hong Kong Region City', v4])
      expect(ipForChina.value.ipWithPrivacy[1]).toBe('192.0.2.3')
      mount()
      await nextTick()
      expect(serviceElement('global').querySelector('.network-ip-provider-name')!.textContent).toBe(
        IP_INFO_API.IPWHOIS,
      )
      expect(getIPInfo).toHaveBeenCalledTimes(2)
    } else {
      expect(getIPInfo).toHaveBeenCalledTimes(1)
      for (const service of [ipForChina.value, ipForGlobal.value]) {
        expect(service.state).toBe('idle')
        expect(service.ipWithPrivacy).toEqual([])
      }
    }
  },
)

it.each([
  { data: { ip: v4, location: [null] } },
  { data: { ip: '999.0.2.3', location: ['China', 'Region'] } },
])('malformed IPIP data fails independently and uses the current locale: %j', async (data) => {
  vi.mocked(getIPFromIpipnetAPI).mockResolvedValue(data as never)
  i18n.global.locale.value = 'zh'
  await getIPs()
  expect(ipForChina.value.state).toBe('failed')
  expect(ipForChina.value.ip[0]).toBe('未获取')
  expect(ipForGlobal.value.state).toBe('ready')
})

it.each([
  { source: IP_INFO_API.IPSB, url: 'https://api.ip.sb/geoip' },
  { source: IP_INFO_API.IPWHOIS, url: 'https://ipwho.is' },
  { source: IP_INFO_API.IPAPI, url: 'https://api.ipapi.is' },
])(
  'self-queries $source and IPIP once without a specified IP or credentials',
  async ({ source, url }) => {
    const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(Response.json({ ip: v4, success: true }))
      .mockResolvedValueOnce(Response.json(chinaResult()))
    IPInfoAPI.value = source
    vi.mocked(getIPInfo).mockImplementation(api.getIPInfo)
    vi.mocked(getIPFromIpipnetAPI).mockImplementation(api.getIPFromIpipnetAPI)
    mount()
    await vi.waitFor(() => expect(isCheckingIPs.value).toBe(false))
    expect(ipForGlobal.value.state).toBe('ready')
    expect(ipForGlobal.value.ipWithPrivacy[1]).toBe(v4)
    expect(ipForChina.value.state).toBe('ready')
    expect(getIPInfo).toHaveBeenCalledExactlyOnceWith('', source, expect.any(AbortSignal))
    expect(fetchMock.mock.calls.map((call) => call[0])).toEqual([url, 'https://myip.ipip.net/json'])
    expect(serviceElement('global').querySelector('.network-ip-provider-name')!.textContent).toBe(
      source,
    )
    for (const call of fetchMock.mock.calls) {
      expect(call[1]).toMatchObject({
        credentials: 'omit',
        referrerPolicy: 'no-referrer',
        cache: 'no-store',
        signal: expect.any(AbortSignal),
      })
    }
  },
)

it.each(['global', 'ipip'] as const)('forwards cancellation to the %s request', async (service) => {
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
    service === 'global'
      ? api.getIPInfo('', IP_INFO_API.IPSB, controller.signal)
      : api.getIPFromIpipnetAPI(controller.signal)
  controller.abort()
  await expect(task).rejects.toThrow('Aborted')
  expect(fetchMock).toHaveBeenCalledTimes(1)
})

it.each(['request', 'body'] as const)(
  'bounds the selected global query %s with an eight-second timeout',
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
      return { ok: true, json: pending } as unknown as Response
    })
    const task = api.getIPInfo('', IP_INFO_API.IPSB)
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
  await expect(api.getIPInfo('', IP_INFO_API.IPSB)).rejects.toThrow('503')
  expect(fetchMock).toHaveBeenCalledTimes(1)
})

it.each([
  {
    apiName: 'IPSB' as const,
    data: {
      isp: 'ISP owner',
      organization: 'Company owner',
      asn: 64500,
      asn_organization: 'ASN owner',
    },
    organization: 'ISP owner',
    url: `https://api.ip.sb/geoip/${v4}`,
  },
  {
    apiName: 'IPSB' as const,
    data: { organization: 'Company owner', asn: 64500, asn_organization: 'ASN owner' },
    organization: 'Company owner',
    url: `https://api.ip.sb/geoip/${v4}`,
  },
  {
    apiName: 'IPSB' as const,
    data: { asn: 64500, asn_organization: 'ASN owner' },
    organization: 'ASN owner',
    url: `https://api.ip.sb/geoip/${v4}`,
  },
  {
    apiName: 'IPWHOIS' as const,
    data: { success: true, connection: { isp: 'ISP owner', asn: 64500, org: 'ASN owner' } },
    organization: 'ISP owner',
    url: `https://ipwho.is/${v4}`,
  },
])(
  'decodes $apiName organization as $organization for an explicit IP',
  async ({ apiName, data, organization, url }) => {
    const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(Response.json({ ip: v4, ...data }))
    expect(await api.getIPInfo(v4, IP_INFO_API[apiName])).toMatchObject({
      ip: v4,
      organization,
      asn: '64500',
      asnOrganization: 'ASN owner',
    })
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(url, expect.any(Object))
  },
)

it('decodes flat, nested and nullable ipapi.is metadata for an explicit IP', async () => {
  const api = await vi.importActual<typeof import('@/api/geoip')>('@/api/geoip')
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
  expect(fetchMock.mock.calls.map((call) => call[0])).toEqual(
    Array(3).fill('https://api.ipapi.is/?q=1.1.1.1'),
  )
})
