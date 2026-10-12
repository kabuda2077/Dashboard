<template>
  <div
    class="relative flex size-full flex-col overflow-hidden"
    :style="!isConnectionCard && padding"
  >
    <template v-if="isConnectionCard">
      <ConnectionCardList />
    </template>
    <template v-else>
      <ConnectionCtrl />
      <ConnectionTable />
    </template>
    <ConnectionDetails />
  </div>
</template>

<script setup lang="ts">
import ConnectionCardList from '@/components/connections/ConnectionCardList.vue'
import ConnectionDetails from '@/components/connections/ConnectionDetails.vue'
import ConnectionTable from '@/components/connections/ConnectionTable.vue'
import ConnectionCtrl from '@/components/controls/ConnectionCtrl.tsx'
import { usePaddingForViews } from '@/composables/use-padding-for-views'
import { isConnectionCard } from '@/store/settings'
const { padding } = usePaddingForViews({
  offsetTop: 0,
  offsetBottom: 0,
})
</script>

<style>
.vjs-tree {
  font-family:
    NotoEmoji,
    Monaco,
    Menlo,
    Consolas,
    Bitstream Vera Sans Mono,
    monospace;
}
.vjs-tree-node.is-highlight,
.vjs-tree-node:hover {
  background-color: var(--color-base-200);
}
</style>
