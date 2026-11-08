import { readonly, ref } from 'vue'

export type ConfirmDialogOptions = {
  title?: string
  message: string
  confirmText?: string
  cancelText?: string
  confirmButtonClass?: string
  checkboxText?: string
}

export type ConfirmDialogResult = {
  confirmed: boolean
  checked: boolean
}

type ConfirmDialogRequest = ConfirmDialogOptions & {
  resolve: (value: ConfirmDialogResult) => void
}

const activeConfirmDialog = ref<ConfirmDialogRequest>()
const confirmDialogQueue: ConfirmDialogRequest[] = []

const showNextConfirmDialog = () => {
  if (activeConfirmDialog.value || confirmDialogQueue.length === 0) return
  activeConfirmDialog.value = confirmDialogQueue.shift()
}

export const confirmDialogState = readonly(activeConfirmDialog)

export const showConfirmDialog = (options: ConfirmDialogOptions, signal?: AbortSignal) =>
  new Promise<ConfirmDialogResult>((resolve) => {
    if (signal?.aborted) {
      resolve({ confirmed: false, checked: false })
      return
    }
    const finish = (result: ConfirmDialogResult) => {
      signal?.removeEventListener('abort', onAbort)
      resolve(result)
    }
    const request = { ...options, resolve: finish }
    const onAbort = () => {
      const index = confirmDialogQueue.indexOf(request)
      if (index >= 0) confirmDialogQueue.splice(index, 1)
      if (activeConfirmDialog.value?.resolve === finish) activeConfirmDialog.value = undefined
      finish({ confirmed: false, checked: false })
      showNextConfirmDialog()
    }
    signal?.addEventListener('abort', onAbort, { once: true })
    confirmDialogQueue.push(request)
    showNextConfirmDialog()
  })

export const resolveConfirmDialog = (confirmed: boolean, checked = false) => {
  const current = activeConfirmDialog.value
  if (!current) return

  activeConfirmDialog.value = undefined
  current.resolve({ confirmed, checked })
  showNextConfirmDialog()
}
