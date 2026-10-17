<template>
  <div class="network-ip-card bg-base-200/30 flex min-w-0 flex-col rounded-xl p-4">
    <div class="flex items-center justify-between">
      <div class="text-base-content/60 text-xs font-semibold tracking-wider uppercase">
        {{ $t('networkInfo') }}
      </div>
      <div class="flex gap-1">
        <button
          class="btn btn-ghost btn-xs btn-circle"
          :aria-label="$t('ipScreenshotTip')"
          @click="showPrivacy = !showPrivacy"
          @mouseenter="handlerShowPrivacyTip"
        >
          <EyeIcon
            v-if="showPrivacy"
            class="h-3.5 w-3.5"
          />
          <EyeSlashIcon
            v-else
            class="h-3.5 w-3.5"
          />
        </button>
        <button
          class="btn btn-ghost btn-xs btn-circle"
          :disabled="isCheckingIPs"
          :aria-label="$t('networkTestIPs')"
          @click="getIPs"
        >
          <BoltIcon class="h-3.5 w-3.5" />
        </button>
      </div>
    </div>

    <div class="network-ip-groups">
      <div
        v-for="entry in ipEntries"
        :key="entry.id"
        class="network-ip-group"
        :data-ip-service="entry.id"
      >
        <div class="network-ip-exit">
          <div
            class="text-sm font-medium"
            :title="$t('networkExitHelp')"
          >
            {{ entry.name }}
          </div>
          <div
            class="network-ip-address text-sm leading-5 font-normal"
            :class="entry.data.state === 'ready' ? '' : 'text-base-content/45'"
            role="status"
          >
            <template v-if="entry.data.state === 'ready'">
              {{ (showPrivacy ? entry.data.ipWithPrivacy[1] : entry.data.ip[1]) || '—' }}
            </template>
            <template v-else>
              {{ $t(stateLabels[entry.data.state]) }}
            </template>
          </div>
        </div>
        <div
          v-if="entry.data.state === 'ready' && entry.id === 'cloudflare'"
          class="network-ip-details"
        >
          <div class="network-ip-detail network-ip-location">
            <span class="network-ip-label">{{ $t('networkIPLocationLabel') }}</span>
            <span class="network-ip-value">{{
              (showPrivacy
                ? entry.data.detailsWithPrivacy.location
                : entry.data.details.location) || '—'
            }}</span>
            <span
              v-if="entry.data.locationState === 'loading'"
              class="text-base-content/45 inline-flex items-center gap-1"
              role="status"
            >
              <span class="loading loading-spinner loading-xs" />{{
                $t('networkIPLocationLoading')
              }}
            </span>
            <span
              v-else-if="entry.data.locationState === 'partial'"
              class="text-base-content/45 network-ip-status"
              :title="$t('networkIPLocationPartialHelp')"
              >{{ $t('networkIPLocationPartial') }}</span
            >
          </div>
          <div
            v-if="entry.data.details.organization"
            class="network-ip-detail network-ip-isp"
          >
            <span class="network-ip-label">{{ $t('networkIPISP') }}</span>
            <span class="network-ip-value">{{
              showPrivacy
                ? entry.data.detailsWithPrivacy.organization
                : entry.data.details.organization
            }}</span>
          </div>
          <div
            v-if="entry.data.details.asn"
            class="network-ip-detail network-ip-asn"
          >
            <span class="network-ip-label">{{ $t('networkIPASN') }}</span>
            <span class="network-ip-value">{{
              showPrivacy ? entry.data.detailsWithPrivacy.asn : entry.data.details.asn
            }}</span>
          </div>
        </div>
        <div
          v-else-if="entry.data.state === 'ready'"
          class="network-ip-location network-ip-value network-ip-summary"
        >
          {{ showPrivacy ? entry.data.ipWithPrivacy[0] : entry.data.ip[0] }}
          <span
            v-if="entry.data.locationState === 'loading'"
            class="text-base-content/45 inline-flex items-center gap-1"
            role="status"
          >
            <span class="loading loading-spinner loading-xs" />{{ $t('networkIPLocationLoading') }}
          </span>
          <span
            v-else-if="entry.data.locationState === 'partial'"
            class="text-base-content/45"
            :title="$t('networkIPLocationPartialHelp')"
            >{{ $t('networkIPLocationPartial') }}</span
          >
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { getIPs, ipForChina, ipForGlobal, isCheckingIPs } from '@/helper/overview'
import { useTooltip } from '@/helper/tooltip'
import { autoIPCheck } from '@/store/settings'
import { BoltIcon, EyeIcon, EyeSlashIcon } from '@heroicons/vue/24/outline'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const showPrivacy = ref(false)
const stateLabels = {
  idle: 'networkNotTested',
  loading: 'getting',
  failed: 'networkIPQueryFailed',
} as const
const ipEntries = computed(() => [
  { id: 'ipip', name: 'ipip.net', data: ipForChina.value },
  { id: 'cloudflare', name: 'Cloudflare', data: ipForGlobal.value },
])
const { showTip } = useTooltip()
const handlerShowPrivacyTip = (e: Event) => {
  showTip(e, t('ipScreenshotTip'))
}

onMounted(() => {
  if (
    autoIPCheck.value &&
    !isCheckingIPs.value &&
    [ipForChina, ipForGlobal].some((item) => item.value.state === 'idle')
  ) {
    void getIPs()
  }
})
</script>

<style scoped>
.network-ip-card {
  container-type: inline-size;
  container-name: network-ip;
  --network-ip-label-width: 6rem;
  min-height: 16.5rem;
}
.network-ip-groups {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 0.875rem;
  margin-top: 0.875rem;
}
.network-ip-group {
  display: flex;
  min-width: 0;
  flex-direction: column;
}
.network-ip-group + .network-ip-group {
  border-top: 1px solid color-mix(in srgb, var(--color-base-content) 6%, transparent);
  padding-top: 0.875rem;
}
.network-ip-address,
.network-ip-value,
.network-ip-location {
  overflow-wrap: anywhere;
}
.network-ip-address,
.network-ip-value,
.network-ip-location {
  font-size: 0.875rem;
  line-height: 1.25rem;
  font-weight: 400;
}
.network-ip-address,
.network-ip-value {
  font-variant-numeric: tabular-nums;
}
.network-ip-exit,
.network-ip-detail {
  display: grid;
  grid-template-columns: var(--network-ip-label-width) minmax(0, 1fr);
  column-gap: 0.75rem;
  align-items: baseline;
  min-width: 0;
}
.network-ip-summary {
  margin-top: 0.375rem;
  padding-left: calc(var(--network-ip-label-width) + 0.75rem);
}
.network-ip-details {
  display: flex;
  flex-direction: column;
  gap: 0.625rem;
  margin-top: 0.75rem;
}
.network-ip-label {
  display: block;
  color: color-mix(in srgb, var(--color-base-content) 60%, transparent);
  font-size: 0.8125rem;
  line-height: 1.25rem;
}
.network-ip-value {
  display: block;
  color: var(--color-base-content);
}
.network-ip-detail > [role='status'],
.network-ip-status {
  grid-column: 2;
}
.network-ip-status {
  display: block;
  font-size: 0.75rem;
  line-height: 1rem;
}
@container network-ip (max-width: 360px) {
  .network-ip-exit,
  .network-ip-detail {
    --network-ip-label-width: 4.75rem;
  }
  .network-ip-summary {
    --network-ip-label-width: 4.75rem;
  }
}
</style>
