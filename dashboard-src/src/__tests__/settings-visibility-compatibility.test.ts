import OverviewCard from '@/components/settings/overview/OverviewCard.vue'
import SettingItem from '@/components/settings/SettingItem.vue'
import { expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref } from 'vue'
vi.mock('@/components/overview/ChartsCard.vue', () => ({
  default: () => h('div', { 'data-testid': 'charts' }),
}))
vi.mock('@/components/overview/NetworkCard.vue', () => ({
  default: () => h('div', { 'data-testid': 'network' }),
}))
it('current settings are visible without legacy metadata; real prerequisites still apply', async () => {
  const element = document.createElement('div')
  const show = ref(false)
  const app = createApp({
    render: () =>
      h('div', [h(OverviewCard), h(SettingItem, { when: show.value }, () => 'conditional')]),
  })
  app.mount(element)
  try {
    expect(element.querySelector('[data-testid="charts"]')).not.toBeNull()
    expect(element.querySelector('[data-testid="network"]')).not.toBeNull()
    expect(element.textContent).not.toContain('conditional')
    show.value = true
    await nextTick()
    expect(element.textContent).toContain('conditional')
  } finally {
    app.unmount()
  }
})
