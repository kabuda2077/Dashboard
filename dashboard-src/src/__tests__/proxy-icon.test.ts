import { applyHostIconCache } from '@/composables/hostBridge'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'

// Happy DOM is not a supported DOMPurify environment. Verify the component
// passes markup through sanitation here; real-browser sanitation is checked by
// the isolated R4 browser fixture.
const { sanitize } = vi.hoisted(() => ({ sanitize: vi.fn(() => '<svg><path d="M0 0h1v1z"></path></svg>') }))
vi.mock('dompurify', () => ({ default: { sanitize } }))
import ProxyIcon from '@/components/proxies/ProxyIcon.vue'

const mountedApps: App[] = []
const mountedElements: HTMLElement[] = []

const mountProxyIcon = (icon: string) => {
  const element = document.createElement('div')
  document.body.appendChild(element)

  const app = createApp(ProxyIcon, { icon })
  app.mount(element)
  mountedApps.push(app)
  mountedElements.push(element)

  return element
}

beforeEach(() => {
  sanitize.mockClear()
  applyHostIconCache({})
})

afterEach(() => {
  for (const app of mountedApps.splice(0)) app.unmount()
  for (const element of mountedElements.splice(0)) element.remove()
  applyHostIconCache({})
})

describe('ProxyIcon', () => {
  it('falls back to the original icon when the host cache has no match', () => {
    const icon = 'https://icons.example.test/fallback.png'
    const element = mountProxyIcon(icon)

    expect(element.querySelector('img')?.getAttribute('src')).toBe(icon)
  })

  it('reacts to host cache updates and matches normalized URLs', async () => {
    const icon = 'https://icons.example.test/groups/../cached.png'
    const cachedIcon = 'file:///cached/icons/group.png'
    const element = mountProxyIcon(icon)

    expect(element.querySelector('img')?.getAttribute('src')).toBe(icon)

    applyHostIconCache({
      'https://icons.example.test/cached.png': cachedIcon,
    })
    await nextTick()

    expect(element.querySelector('img')?.getAttribute('src')).toBe(cachedIcon)
    applyHostIconCache({ 'https://icons.example.test/cached.png': 'http://localhost/replacement.png' })
    await nextTick()
    expect(element.querySelector('img')?.getAttribute('src')).toBe('http://localhost/replacement.png')
    applyHostIconCache({})
    await nextTick()
    expect(element.querySelector('img')?.getAttribute('src')).toBe(icon)
  })

  it('sanitizes cached SVG markup before mounting it', async () => {
    const icon = 'https://icons.example.test/group.svg'
    const unsafeSvg = '<svg onload="alert(1)"><script>alert(1)</script><path d="M0 0h1v1z"/></svg>'
    const element = mountProxyIcon(icon)

    applyHostIconCache({
      [icon]: `data:image/svg+xml,${unsafeSvg}`,
    })
    await nextTick()

    expect(sanitize).toHaveBeenCalledWith(unsafeSvg)
    const renderedIcon = element.querySelector('div.inline-block')
    expect(renderedIcon?.querySelector('svg')).not.toBeNull()
    expect(renderedIcon?.querySelector('script')).toBeNull()
    expect(renderedIcon?.querySelector('svg')?.hasAttribute('onload')).toBe(false)
  })
})
