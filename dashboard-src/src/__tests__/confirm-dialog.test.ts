import { confirmDialogState, resolveConfirmDialog, showConfirmDialog } from '@/helper/confirmDialog'
import { describe, expect, it } from 'vitest'

describe('confirm dialog queue', () => {
  it('resolves queued confirmations in order', async () => {
    const first = showConfirmDialog({ message: 'first' })
    const second = showConfirmDialog({ message: 'second' })

    expect(confirmDialogState.value?.message).toBe('first')
    resolveConfirmDialog(true)
    await expect(first).resolves.toBe(true)

    expect(confirmDialogState.value?.message).toBe('second')
    resolveConfirmDialog(false)
    await expect(second).resolves.toBe(false)
    expect(confirmDialogState.value).toBeUndefined()
  })
})
