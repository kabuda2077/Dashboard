import SourceIPLabels from '@/components/settings/connections/SourceIPLabels.vue'
import { showNotification } from '@/helper/notification'
import { sourceIPLabelList } from '@/store/settings'
import { afterEach, expect, it, vi } from 'vitest'
import { createApp, nextTick } from 'vue'

vi.mock('@/store/settings', async () => ({ sourceIPLabelList: (await import('vue')).ref([]) }))
vi.mock('@/store/setup', async () => ({ backendList: (await import('vue')).ref([]) }))
vi.mock('@/store/connections', async () => ({ connections: (await import('vue')).ref([]) }))
vi.mock('@/helper', () => ({ getConnectionSourceIP: vi.fn() }))
vi.mock('@/helper/notification', () => ({ showNotification: vi.fn() }))
vi.mock('@/helper/tooltip', () => ({ checkTruncation: vi.fn(), useTooltip: () => ({ showTip: vi.fn() }) }))
vi.mock('@/helper/utils', () => ({ getLabelFromBackend: vi.fn() }))
vi.mock('@/composables/use-swipe-router', async () => ({ disableSwipe: (await import('vue')).ref(false) }))
vi.mock('@/components/common/TextInput.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    props: ['modelValue'],
    emits: ['update:modelValue'],
    setup: (props, { emit }) => () => h('input', {
      value: props.modelValue,
      onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
    }),
  }) }
})
vi.mock('@/components/common/DialogWrapper.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ setup: (_, { slots }) => () => h('div', slots.default?.()) }) }
})
vi.mock('vuedraggable', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    props: ['modelValue'],
    setup: (props, { slots }) => () => h('div', props.modelValue.map((element: unknown) => slots.item?.({ element }))),
  }) }
})

afterEach(() => {
  vi.clearAllMocks()
  sessionStorage.clear()
})

it('keeps invalid add/edit drafts out of storage and saves a corrected nested input edit', async () => {
  sourceIPLabelList.value = [{ id: 'saved', key: '192.0.2.10', label: 'desktop' }]
  const root = document.createElement('div')
  document.body.append(root)
  const app = createApp(SourceIPLabels, { modelValue: true })
  app.config.globalProperties.$t = (key: string) => key
  app.mount(root)
  const change = async (index: number, value: string) => {
    const input = root.querySelectorAll('input')[index]!
    input.value = value
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await nextTick()
  }
  try {
    await change(0, '/[')
    await change(1, 'broken new label')
    root.querySelector<HTMLButtonElement>('.btn-circle')!.click()
    await nextTick()
    expect(sourceIPLabelList.value).toHaveLength(1)
    expect(showNotification).toHaveBeenCalledOnce()
    await change(0, '')

    await change(2, '/[')
    expect(sourceIPLabelList.value[0].key).toBe('192.0.2.10')
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('/[')
    await change(3, 'new name')
    expect(sourceIPLabelList.value[0].label).toBe('desktop')
    expect(showNotification).toHaveBeenCalledOnce()

    await change(2, '/^192')
    expect(sourceIPLabelList.value[0]).toEqual({ id: 'saved', key: '/^192', label: 'new name' })
    expect(root.querySelector('[role="alert"]')).toBeNull()
  } finally {
    app.unmount()
    root.remove()
  }
})

it('shows a stored invalid regex beside its row and allows correcting it', async () => {
  sourceIPLabelList.value = [{ id: 'broken', key: '/[', label: 'old label' }]
  const root = document.createElement('div')
  document.body.append(root)
  const app = createApp(SourceIPLabels, { modelValue: true })
  app.config.globalProperties.$t = (key: string) => key
  app.mount(root)
  try {
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('/[')
    const input = root.querySelectorAll('input')[2]!
    input.value = '/fixed'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await nextTick()
    expect(sourceIPLabelList.value[0].key).toBe('/fixed')
    expect(root.querySelector('[role="alert"]')).toBeNull()
  } finally {
    app.unmount()
    root.remove()
  }
})
