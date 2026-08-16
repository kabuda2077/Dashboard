import { confirmDialogState, resolveConfirmDialog, showConfirmDialog } from '@/helper/confirmDialog'
import { describe, expect, it } from 'vitest'

describe('confirm dialog queue', () => {
  it('resolves queued confirmations in order', async () => {
    const first = showConfirmDialog({ message: 'first' })
    const second = showConfirmDialog({ message: 'second' })

    expect(confirmDialogState.value?.message).toBe('first')
    resolveConfirmDialog(true)
    await expect(first).resolves.toEqual({ confirmed: true, checked: false })

    expect(confirmDialogState.value?.message).toBe('second')
    resolveConfirmDialog(false, true)
    await expect(second).resolves.toEqual({ confirmed: false, checked: true })
    expect(confirmDialogState.value).toBeUndefined()
  })
})
