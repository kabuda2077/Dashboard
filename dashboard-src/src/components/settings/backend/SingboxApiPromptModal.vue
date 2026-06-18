<template>
  <DialogWrapper
    v-model="isVisible"
    :title="t('enableSingboxApi')"
    @enter="!isSaving && handleSave()"
  >
    <div class="flex flex-col gap-4">
      <p class="text-sm opacity-70">{{ t('enableSingboxApiTip') }}</p>

      <div class="flex flex-col gap-1">
        <label class="text-sm">{{ t('protocol') }}</label>
        <select
          class="select select-sm w-full"
          v-model="form.protocol"
        >
          <option value="http">HTTP</option>
          <option value="https">HTTPS</option>
        </select>
      </div>

      <div class="flex flex-col gap-1">
        <label class="text-sm">{{ t('host') }}</label>
        <TextInput
          class="w-full"
          v-model="form.host"
          placeholder="127.0.0.1"
        />
      </div>

      <div class="flex flex-col gap-1">
        <label class="text-sm">{{ t('port') }}</label>
        <TextInput
          class="w-full"
          v-model="form.port"
          placeholder="9090"
        />
      </div>

      <div class="flex flex-col gap-1">
        <label class="text-sm">{{ t('password') }} ({{ t('optional') }})</label>
        <input
          type="password"
          class="input input-sm w-full"
          v-model="form.secret"
        />
      </div>

      <div class="flex justify-end gap-2">
        <button
          class="btn btn-sm"
          @click="finish"
          :disabled="isSaving"
        >
          {{ t('skip') }}
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
import { isSingboxChannelAvailable } from '@/api'
import DialogWrapper from '@/components/common/DialogWrapper.vue'
import TextInput from '@/components/common/TextInput.vue'
import { ROUTE_NAME } from '@/constant'
import { showNotification } from '@/helper/notification'
import router from '@/router'
import { backendList, updateBackend } from '@/store/setup'
import type { Backend, SingboxChannel } from '@/types'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ modelValue: boolean; backendUuid: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', value: boolean): void }>()
const { t } = useI18n()
const isFinishing = ref(false)

const isVisible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => {
    if (value) {
      emit('update:modelValue', true)
    } else {
      void finish()
    }
  },
})

const form = reactive<SingboxChannel>({
  protocol: 'http',
  host: '127.0.0.1',
  port: '9090',
  secret: '',
})
const isSaving = ref(false)
const currentBackend = computed(() =>
  backendList.value.find((backend) => backend.uuid === props.backendUuid),
)

watch(
  () => props.modelValue,
  (isOpen) => {
    if (!isOpen) return
    form.protocol = 'http'
    form.host = currentBackend.value?.host || '127.0.0.1'
    form.port = '9090'
    form.secret = ''
  },
)

const finish = async () => {
  if (isFinishing.value) return
  isFinishing.value = true
  emit('update:modelValue', false)
  router.push({ name: ROUTE_NAME.proxies })
  isFinishing.value = false
}

const handleSave = async () => {
  const backend = currentBackend.value
  if (!backend || !form.host || !form.port) return
  isSaving.value = true

  try {
    const candidate: Backend = {
      ...backend,
      singboxChannel: { ...form },
    }
    if (!(await isSingboxChannelAvailable(candidate, 10000))) {
      showNotification({ content: t('singboxConnectionFailed'), type: 'alert-error' })
      return
    }

    const { uuid, ...persisted } = candidate
    updateBackend(uuid, persisted)
    showNotification({ content: t('backendConfigSaved'), type: 'alert-success' })
    await finish()
  } finally {
    isSaving.value = false
  }
}
</script>
