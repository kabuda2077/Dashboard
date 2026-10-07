<template>
  <div class="setting-panel-row core-profile-row">
    <label
      class="core-field-label"
      :for="`${idPrefix}-exe`"
      >{{ t('desktop.executable') }}</label
    >
    <div class="flex gap-2">
      <input
        :id="`${idPrefix}-exe`"
        v-model="draft.values.exePath"
        class="input input-sm dashboard-input"
      />
      <button
        class="btn btn-sm dashboard-action-btn"
        :disabled="busy"
        @click="$emit('choose', false)"
      >
        {{ t('desktop.choose') }}
      </button>
      <button
        v-if="!compact"
        class="btn btn-sm dashboard-action-btn"
        @click="$emit('location', false)"
      >
        {{ t('desktop.location') }}
      </button>
    </div>
  </div>
  <div class="setting-panel-row core-profile-row">
    <label
      class="core-field-label"
      :for="`${idPrefix}-config`"
      >{{ t('desktop.config') }}</label
    >
    <div class="flex gap-2">
      <input
        :id="`${idPrefix}-config`"
        v-model="draft.values.configPath"
        class="input input-sm dashboard-input"
      />
      <button
        class="btn btn-sm dashboard-action-btn"
        :disabled="busy"
        @click="$emit('choose', true)"
      >
        {{ t('desktop.choose') }}
      </button>
      <button
        v-if="!compact"
        class="btn btn-sm dashboard-action-btn"
        @click="$emit('location', true)"
      >
        {{ t('desktop.location') }}
      </button>
    </div>
  </div>
  <div class="setting-panel-row core-profile-row">
    <label
      class="core-field-label"
      :for="`${idPrefix}-api`"
      >{{ t('desktop.apiAddress') }}</label
    >
    <div class="flex gap-2">
      <input
        :id="`${idPrefix}-api`"
        v-model="draft.values.apiUrl"
        class="input input-sm dashboard-input w-full"
      />
      <button
        v-if="!compact"
        class="btn btn-sm dashboard-action-btn"
        @click="$emit('retry')"
      >
        {{ t('desktop.retry') }}
      </button>
    </div>
  </div>
  <div class="setting-panel-row core-profile-row">
    <label
      class="core-field-label"
      :for="`${idPrefix}-secret`"
      >Secret</label
    >
    <div class="flex min-w-0 gap-2">
      <div
        class="relative flex min-w-0 flex-1"
        @focusout="concealOnLeave"
      >
        <input
          :id="`${idPrefix}-secret`"
          v-model="draft.values.secret"
          :type="secretVisible ? 'text' : 'password'"
          autocomplete="off"
          class="input input-sm dashboard-input w-full pr-9"
          :disabled="draft.base.secretDecryptionFailed && !draft.replaceSecret"
        />
        <button
          type="button"
          class="btn btn-ghost btn-xs absolute top-1/2 right-1 h-6 min-h-6 w-6 -translate-y-1/2 p-0"
          :aria-label="t(secretVisible ? 'desktop.hideSecret' : 'desktop.showSecret')"
          :title="t(secretVisible ? 'desktop.hideSecret' : 'desktop.showSecret')"
          :aria-pressed="secretVisible"
          :aria-controls="`${idPrefix}-secret`"
          :disabled="draft.base.secretDecryptionFailed && !draft.replaceSecret"
          @click="secretVisible = !secretVisible"
        >
          <component
            :is="secretVisible ? EyeSlashIcon : EyeIcon"
            class="h-4 w-4"
          />
        </button>
      </div>
      <slot name="secret-action" />
    </div>
    <label
      v-if="draft.base.secretDecryptionFailed"
      class="core-secret-warning text-warning flex items-center gap-2 text-sm"
    >
      <input
        v-model="draft.replaceSecret"
        type="checkbox"
        class="checkbox checkbox-sm"
      />{{ t('desktop.replaceSecret') }}
    </label>
  </div>
</template>

<script setup lang="ts">
import type { CoreDraft } from '@/helper/hostDraft'
import { i18n } from '@/i18n'
import { EyeIcon, EyeSlashIcon } from '@heroicons/vue/24/outline'
import { ref, watch } from 'vue'
const t = i18n.global.t

const props = defineProps<{
  draft: CoreDraft
  idPrefix: string
  busy: boolean
  compact?: boolean
}>()
const secretVisible = ref(false)
watch(
  () => props.draft,
  () => {
    secretVisible.value = false
  },
)
const concealOnLeave = (event: FocusEvent) => {
  if (
    !(event.relatedTarget instanceof Node) ||
    !(event.currentTarget as HTMLElement).contains(event.relatedTarget)
  )
    secretVisible.value = false
}
defineEmits<{ choose: [config: boolean]; location: [config: boolean]; retry: [] }>()
</script>

<style scoped>
/* Use one reveal control in Edge/WebView, not a second native eye underneath it. */
input::-ms-reveal {
  display: none;
}
</style>
