<template>
  <div class="network-ip-card flex min-w-0 flex-col">
    <div class="network-panel-header">
      <div class="text-base-content/60 text-xs font-semibold tracking-wider uppercase">
        {{ $t('networkInfo') }}
      </div>
      <div class="network-header-actions">
        <button
          class="network-icon-button"
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
          class="network-icon-button"
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
            class="network-ip-provider"
            :title="$t('networkExitHelp')"
          >
            <span class="network-ip-provider-name">{{ entry.name }}</span>
            <span class="network-tile-badge">
              {{ $t(entry.id === 'ipip' ? 'networkDomestic' : 'networkInternational') }}
            </span>
          </div>
          <div
            class="network-ip-address"
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
          v-if="entry.data.state === 'ready'"
          :class="
            entry.id === 'global' ||
            entry.data.details.organization ||
            entry.data.detailsWithPrivacy.organization
              ? 'network-ip-details'
              : 'network-ip-row'
          "
        >
          <div class="network-ip-detail network-ip-location">
            <span class="network-ip-label">{{ $t('networkIPLocationLabel') }}</span>
            <span
              class="network-ip-value"
              :title="entry.location"
              >{{ entry.location }}</span
            >
          </div>
          <div
            v-if="
              (entry.data.details.asn || entry.data.detailsWithPrivacy.asn) && entry.id === 'global'
            "
            class="network-ip-detail network-ip-asn"
          >
            <span class="network-ip-label">{{ $t('networkIPASN') }}</span>
            <span
              class="network-ip-value"
              :title="entry.asn"
              >{{ entry.asnNumber }}</span
            >
          </div>
          <div
            v-if="entry.data.details.organization || entry.data.detailsWithPrivacy.organization"
            class="network-ip-detail network-ip-isp"
          >
            <span class="network-ip-label">{{ $t('networkIPISP') }}</span>
            <span
              class="network-ip-value"
              :title="entry.isp"
              >{{ entry.isp }}</span
            >
          </div>
          <span
            v-if="entry.data.locationState === 'partial'"
            class="text-base-content/45 network-ip-status"
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
import { autoIPCheck, IPInfoAPI } from '@/store/settings'
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
const ipEntries = computed(() =>
  [
    { id: 'ipip', name: 'ipip.net', data: ipForChina.value },
    { id: 'global', name: IPInfoAPI.value, data: ipForGlobal.value },
  ].map((entry) => {
    const details = showPrivacy.value ? entry.data.detailsWithPrivacy : entry.data.details
    const address = showPrivacy.value ? entry.data.ipWithPrivacy : entry.data.ip
    return {
      ...entry,
      location: details.location || address[0] || '—',
      isp: details.organization,
      asn: details.asn,
      asnNumber: details.asn.split(/\s+/, 1)[0],
    }
  }),
)
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
}
.network-ip-groups {
  display: grid;
  flex: 1;
  grid-template-rows: repeat(2, minmax(0, 1fr));
  gap: 0.625rem;
  margin-top: 0.625rem;
}
.network-ip-group {
  display: flex;
  flex: 1;
  min-width: 0;
  flex-direction: column;
  justify-content: space-between;
  border-radius: 0.6875rem;
  padding: 0.75rem 1rem 0.625rem;
  background: var(--network-surface);
  gap: 0.5rem;
}
.network-ip-exit {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.75rem;
  min-width: 0;
}
.network-ip-provider {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  flex-shrink: 0;
}
.network-ip-provider-name {
  font-size: 0.84375rem;
  font-weight: 500;
}
.network-ip-address {
  min-width: 0;
  font-size: 0.84375rem;
  font-weight: 450;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.network-ip-details,
.network-ip-row {
  display: grid;
  grid-template-columns: minmax(0, 1.6fr) minmax(0, 5.5rem) minmax(0, 1fr);
  gap: 0.75rem;
  align-items: end;
}
.network-ip-detail {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  min-width: 0;
}
.network-ip-location {
  grid-column: 1;
}
.network-ip-asn {
  grid-column: 2;
}
.network-ip-isp {
  grid-column: 3;
  text-align: right;
  align-items: flex-end;
}
.network-ip-label {
  display: block;
  color: color-mix(in srgb, var(--color-base-content) 42%, transparent);
  font-size: 0.6875rem;
}
.network-ip-value {
  display: block;
  color: color-mix(in srgb, var(--color-base-content) 60%, transparent);
  font-size: 0.78125rem;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}
.network-ip-status {
  grid-column: 1 / -1;
  font-size: 0.75rem;
  line-height: 1rem;
}
</style>
