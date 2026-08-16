import { createApp, defineComponent } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

describe('keyboard shortcuts', () => {
  beforeEach(() => {
    vi.resetModules()
    localStorage.clear()
  })

  it('toggles manage hidden groups with the default H shortcut', async () => {
    const { default: router } = await import('@/router')
    const { useKeyboard } = await import('@/composables/keyboard')
    const { manageHiddenGroup } = await import('@/store/settings')

    manageHiddenGroup.value = false

    const app = createApp(
      defineComponent({
        setup() {
          useKeyboard()
          return () => null
        },
      }),
    )
    app.use(router)
    const el = document.createElement('div')
    document.body.appendChild(el)
    app.mount(el)

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'h' }))

    expect(manageHiddenGroup.value).toBe(true)

    app.unmount()
    el.remove()
  })
})
