<template>
  <DialogWrapper
    v-model="isVisible"
    :title="t('editBackendTitle')"
    @enter="!isSaving && handleSave()"
  >
    <div class="flex flex-col gap-4">
      <div class="flex flex-col gap-1">
        <label class="text-sm">{{ t('selectBackend') }}</label>
        <select
          class="select select-sm w-full"
          v-model="selectedBackendUuid"
        >
          <option
            v-for="backend in backendList"
            :key="backend.uuid"
            :value="backend.uuid"
          >
            {{ getLabelFromBackend(backend) }}
          </option>
        </select>
      </div>

      <div
        v-if="editForm"
        class="flex flex-col gap-3"
      >
        <div class="divider my-0 text-xs">{{ t('clashApi') }}</div>

        <div class="flex gap-2">
          <div class="flex w-24 flex-none flex-col gap-1">
            <label class="text-sm">{{ t('protocol') }}</label>
            <select
              class="select select-sm w-full"
              v-model="editForm.protocol"
            >
              <option value="http">HTTP</option>
              <option value="https">HTTPS</option>
            </select>
          </div>
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <label class="text-sm">{{ t('host') }}</label>
            <TextInput
              class="w-full"
              name="username"
              v-model="editForm.host"
              placeholder="127.0.0.1"
            />
          </div>
          <div class="flex w-20 flex-none flex-col gap-1">
            <label class="text-sm">{{ t('port') }}</label>
            <TextInput
              class="w-full"
              v-model="editForm.port"
              placeholder="9090"
            />
          </div>
        </div>

        <div class="flex gap-2">
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <label class="truncate text-sm">{{ t('secondaryPath') }} ({{ t('optional') }})</label>
            <TextInput
              class="w-full"
              v-model="editForm.secondaryPath"
              :placeholder="t('optional')"
            />
          </div>
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <label class="truncate text-sm">{{ t('label') }} ({{ t('optional') }})</label>
            <TextInput
              class="w-full"
              v-model="editForm.label"
              :placeholder="t('label')"
            />
          </div>
        </div>

        <div class="flex flex-col gap-1">
          <label class="text-sm">{{ t('password') }}</label>
          <input
            type="password"
            class="input input-sm w-full"
            v-model="editForm.password"
          />
        </div>
      </div>

      <div class="flex justify-end gap-2">
        <button
          class="btn btn-sm"
          @click="handleCancel"
          :disabled="isSaving"
        >
          {{ t('cancel') }}
        </button>
        <button
          class="btn btn-primary btn-sm"
          @click="handleSave"
          :disabled="isSaving"
        >
          <span
            v-if="isSaving"
            class="loading loading-spinner loading-xs"
          ></span>
          {{ isSaving ? t('checking') : t('save') }}
        </button>
      </div>
    </div>
  </DialogWrapper>
</template>

<script setup lang="ts">
import { isBackendAvailable } from '@/assembly/backend'
import DialogWrapper from '@/components/common/DialogWrapper.vue'
import TextInput from '@/components/common/TextInput.vue'
import { showNotification } from '@/helper/notification'
import { getLabelFromBackend } from '@/helper/utils'
import { activeBackend, backendList, updateBackend } from '@/store/setup'
import type { Backend } from '@/types'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  modelValue: boolean
  defaultBackendUuid?: string
}

interface Emits {
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved'): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()
const { t } = useI18n()

const isVisible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const editForm = ref<Omit<Backend, 'uuid'> | null>(null)
const selectedBackendUuid = ref('')
const isSaving = ref(false)

const selectedBackend = computed(
  () => backendList.value.find((backend) => backend.uuid === selectedBackendUuid.value) || null,
)

watch(
  () => props.modelValue,
  (isOpen) => {
    if (!isOpen) return
    selectedBackendUuid.value =
      props.defaultBackendUuid || activeBackend.value?.uuid || backendList.value[0]?.uuid || ''
  },
  { immediate: true },
)

watch(
  selectedBackend,
  (backend) => {
    if (!backend) return
    editForm.value = {
      type: backend.type,
      protocol: backend.protocol,
      host: backend.host,
      port: backend.port,
      secondaryPath: backend.secondaryPath,
      password: backend.password,
      label: backend.label || '',
      disableUpgradeCore: backend.disableUpgradeCore || false,
      disableTunMode: backend.disableTunMode || false,
    }
  },
  { immediate: true },
)

const reset = () => {
  editForm.value = null
  selectedBackendUuid.value = ''
}

const handleCancel = () => {
  isVisible.value = false
  reset()
}

const handleSave = async () => {
  if (!editForm.value || !selectedBackend.value) return
  if (isSaving.value) return
  isSaving.value = true
  const target = selectedBackend.value.uuid
  const draft = editForm.value

  try {
    const composed: Omit<Backend, 'uuid'> = { ...draft }
    const testBackend: Backend = { uuid: target, ...composed }
    const available = await isBackendAvailable(testBackend, 10000)
    if (!props.modelValue || selectedBackendUuid.value !== target || editForm.value !== draft
      || Object.keys(composed).some((key) => composed[key as keyof typeof composed] !== draft[key as keyof typeof draft])) return

    if (!available) {
      showNotification({ content: t('backendConnectionFailed'), type: 'alert-error' })
      return
    }

    updateBackend(target, composed)
    showNotification({ content: t('backendConfigSaved'), type: 'alert-success' })
    isVisible.value = false
    reset()
    emit('saved')
  } catch (error) {
    showNotification({
      content: `${t('saveFailed')}: ${error}`,
      type: 'alert-error',
    })
  } finally {
    isSaving.value = false
  }
}
</script>
