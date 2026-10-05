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
      <input
        :id="`${idPrefix}-secret`"
        v-model="draft.values.secret"
        type="password"
        autocomplete="off"
        class="input input-sm dashboard-input w-full"
        :disabled="draft.base.secretDecryptionFailed && !draft.replaceSecret"
      />
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
const t = i18n.global.t

defineProps<{ draft: CoreDraft; idPrefix: string; busy: boolean; compact?: boolean }>()
defineEmits<{ choose: [config: boolean]; location: [config: boolean]; retry: [] }>()
</script>
