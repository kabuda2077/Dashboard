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

export const showConfirmDialog = (options: ConfirmDialogOptions) =>
  new Promise<ConfirmDialogResult>((resolve) => {
    confirmDialogQueue.push({ ...options, resolve })
    showNextConfirmDialog()
  })

export const resolveConfirmDialog = (confirmed: boolean, checked = false) => {
  const current = activeConfirmDialog.value
  if (!current) return

  activeConfirmDialog.value = undefined
  current.resolve({ confirmed, checked })
  showNextConfirmDialog()
}
