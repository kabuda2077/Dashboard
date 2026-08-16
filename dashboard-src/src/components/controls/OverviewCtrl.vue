<template>
  <CtrlsBar>
    <div class="flex h-9 flex-row items-center gap-2 text-sm">
      <button
        class="btn btn-circle btn-sm"
        @click="showCardSettingsDialog = true"
      >
        <Cog6ToothIcon class="h-4 w-4" />
      </button>
    </div>
    <OverviewCardSettingsDialog v-model="showCardSettingsDialog" />
  </CtrlsBar>
</template>

<script setup lang="ts">
import OverviewCardSettingsDialog from '@/components/overview/OverviewCardSettingsDialog.vue'
import { useCtrlsBar } from '@/composables/useCtrlsBar'
import { Cog6ToothIcon } from '@heroicons/vue/24/outline'
import { ref } from 'vue'
import CtrlsBar from '../common/CtrlsBar.vue'

const showCardSettingsDialog = ref(false)

useCtrlsBar()
</script>
