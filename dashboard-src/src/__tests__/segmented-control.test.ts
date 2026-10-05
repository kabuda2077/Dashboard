import { expect, it } from 'vitest'
import { createApp, h, nextTick, ref } from 'vue'
import SegmentedControl from '@/components/common/SegmentedControl.vue'
it('tabs expose selection and support arrows, Home and End with roving focus', async () => {
  const selected = ref('a')
  const root = document.createElement('div'); document.body.append(root)
  const app = createApp({ render: () => h(SegmentedControl, {
    modelValue: selected.value, 'onUpdate:modelValue': (value: string) => { selected.value = value },
    options: ['a', 'b', 'c'].map((value) => ({ value, label: value })),
  }) })
  app.mount(root)
  try {
    const buttons = [...root.querySelectorAll<HTMLButtonElement>('[role=tab]')]
    expect(buttons.map((button) => button.tabIndex)).toEqual([0, -1, -1])
    buttons[0].focus()
    for (const [key, value] of [['ArrowRight', 'b'], ['End', 'c'], ['Home', 'a'], ['ArrowLeft', 'c']]) {
      document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))
      await nextTick(); await nextTick()
      expect(selected.value).toBe(value)
      expect((document.activeElement as HTMLElement).dataset.value).toBe(value)
      expect(root.querySelectorAll('[aria-selected=true]')).toHaveLength(1)
    }
  } finally { app.unmount(); root.remove() }
})
