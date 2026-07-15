<template>
  <DialogWrapper
    v-if="isReady"
    :model-value="!!confirmDialogState"
    :title="confirmDialogState?.title"
    box-class="max-w-lg"
    @update:model-value="handleModelValue"
    @enter="handleConfirm"
  >
    <div
      v-if="confirmDialogState"
      class="flex max-h-[65dvh] min-h-0 flex-col gap-4 p-2 max-md:max-h-[50dvh]"
    >
      <div class="min-h-0 overflow-y-auto overscroll-contain pr-1">
        <p class="text-sm break-all whitespace-pre-wrap">
          {{ confirmDialogState.message }}
        </p>
      </div>
      <div class="flex shrink-0 justify-end gap-2">
        <button
          class="btn btn-sm"
          @click="handleCancel"
        >
          {{ confirmDialogState.cancelText || $t('cancel') }}
        </button>
        <button
          :class="['btn btn-sm', confirmDialogState.confirmButtonClass || 'btn-primary']"
          @click="handleConfirm"
        >
          {{ confirmDialogState.confirmText || $t('confirm') }}
        </button>
      </div>
    </div>
  </DialogWrapper>
</template>

<script setup lang="ts">
import { confirmDialogState, resolveConfirmDialog } from '@/helper/confirmDialog'
import { onMounted, ref } from 'vue'
import DialogWrapper from './DialogWrapper.vue'

const isReady = ref(false)

onMounted(() => {
  isReady.value = true
})

const handleModelValue = (value: boolean | undefined) => {
  if (!value) handleCancel()
}

const handleCancel = () => resolveConfirmDialog(false)
const handleConfirm = () => resolveConfirmDialog(true)
</script>
