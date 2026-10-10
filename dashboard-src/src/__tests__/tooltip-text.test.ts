import type { Instance } from 'tippy.js'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'

beforeEach(() => {
  vi.resetModules()
  document.body.innerHTML = '<div id="app-content"></div>'
})
afterEach(() => {
  for (const target of document.querySelectorAll<HTMLElement>('[data-tooltip-test]'))
    (target as HTMLElement & { _tippy?: Instance })._tippy?.destroy()
  document.body.replaceChildren()
})

it('renders malicious truncated names as text through the real tooltip consumer', async () => {
  const { checkTruncation } = await import('@/helper/tooltip')
  const target = document.createElement('div')
  const text = '<img src=x onerror="window.__tooltipExecuted=true">long external node name'
  target.dataset.tooltipTest = ''
  target.innerText = text
  Object.defineProperties(target, {
    scrollWidth: { value: 800 },
    clientWidth: { value: 100 },
  })
  document.querySelector('#app-content')!.append(target)
  target.addEventListener('mouseenter', checkTruncation)
  target.dispatchEvent(new MouseEvent('mouseenter'))

  await vi.waitFor(
    () => {
      const content = document.querySelector('.tippy-content')!
      expect(content).not.toBeNull()
      expect(content.textContent).toBe(text)
      expect(content.querySelector('img, [onerror]')).toBeNull()
    },
    { timeout: 2000 },
  )
  expect(Reflect.get(window, '__tooltipExecuted')).toBeUndefined()
})

it('retains DOM rich content and treats later string updates as text', async () => {
  const { useTooltip } = await import('@/helper/tooltip')
  const target = document.createElement('button')
  target.dataset.tooltipTest = ''
  document.querySelector('#app-content')!.append(target)
  const rich = document.createElement('strong')
  rich.textContent = 'latency history'
  const tooltip = useTooltip()
  target.addEventListener('mouseenter', (event) => tooltip.showTip(event, rich))
  target.dispatchEvent(new MouseEvent('mouseenter'))
  await vi.waitFor(() => expect(document.querySelector('.tippy-content strong')).toBe(rich))
  tooltip.updateTip('<img src=x onerror="alert(1)">')
  const content = document.querySelector('.tippy-content')!
  expect(content.textContent).toBe('<img src=x onerror="alert(1)">')
  expect(content.querySelector('img')).toBeNull()
})
