import OverviewCard from '@/components/settings/overview/OverviewCard.vue'
import { OVERVIEW_ITEM_KEYS } from '@/config/settingsItems'
import { hiddenSettingsItems } from '@/store/settings'
import { createApp, h, nextTick, type App } from 'vue'
import { afterEach, expect, it, vi } from 'vitest'

vi.mock('@/components/overview/ChartsCard.vue', () => ({
  default: () => h('div', { 'data-testid': 'charts-card' }),
}))

vi.mock('@/components/overview/NetworkCard.vue', () => ({
  default: () => h('div', { 'data-testid': 'network-card' }),
}))

let app: App | undefined
let host: HTMLElement | undefined

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = undefined
  host = undefined
  hiddenSettingsItems.value = {}
  localStorage.clear()
})

it('keeps legacy hidden-setting data reactive after removing edit tools', async () => {
  hiddenSettingsItems.value = {
    [OVERVIEW_ITEM_KEYS.chartsCard]: true,
  }
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp(OverviewCard)
  app.mount(host)

  expect(host.querySelector('[data-testid="charts-card"]')).toBeNull()
  expect(host.querySelector('[data-testid="network-card"]')).not.toBeNull()

  hiddenSettingsItems.value = {
    [OVERVIEW_ITEM_KEYS.networkCard]: true,
  }
  await nextTick()

  expect(host.querySelector('[data-testid="charts-card"]')).not.toBeNull()
  expect(host.querySelector('[data-testid="network-card"]')).toBeNull()
})
