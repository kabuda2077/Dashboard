<template>
  <div
    ref="columnRef"
    class="relative w-full"
    :style="{ height: `${totalSize}px` }"
  >
    <!--
      定位用 top 而不是 transform:手机端展开的组卡片里有个 fixed inset-0 的遮罩,
      祖先只要带 transform 就会变成它的包含块,遮罩就不再是全屏的了。
    -->
    <div
      v-for="row in virtualRows"
      :key="row.key.toString()"
      :data-index="row.index"
      :data-proxy-page-item="data[row.index]"
      :ref="(el) => measureRow(el as Element | null)"
      class="absolute inset-x-0"
      :style="{ top: `${row.start - scrollMargin}px` }"
    >
      <slot
        :item="data[row.index]"
        :index="row.index"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { createVirtualRowShift, provideVirtualRowShift } from '@/composables/virtualRowShift'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { computed, nextTick, onBeforeUnmount, ref } from 'vue'

/*
 * 挂在「外部滚动容器」上的虚拟列表列。
 *
 * 和 VirtualScroller 的区别只有一条:那个组件自己就是滚动容器,这个组件把滚动容器
 * 当参数收进来。代理页需要这样 —— 页面自己要拿滚动元素做位置保存/恢复,双列模式下
 * 还要两列共用同一个滚动条(各自一个 virtualizer,按 index % 2 分好的数组各滚各的)。
 */
const props = withDefaults(
  defineProps<{
    data: string[]
    // 外部滚动容器;首帧还没挂上时是 null,virtualizer 会等它出现
    scrollElement: HTMLElement | null
    // 本列在滚动内容里的起始偏移(上面还有控制栏等),不给的话可视区算出来会整体偏移
    scrollMargin: number
    // 没量过的卡片用的估算高度
    estimateSize: number
    // 量到的高度按这个前缀存,跨路由回来时列表总高度不会先塌再长
    sizeCacheKey: string
    gap?: number
    overscan?: number
  }>(),
  {
    gap: 12,
    overscan: 4,
  },
)

/*
 * 量到的卡片高度按「卡片形态 + 组名」缓存在模块作用域,组件卸载时写入。
 * virtualizer 自己的 itemSizeCache 随实例一起销毁,跨路由回来就没了 ——
 * 那样恢复滚动位置时所有卡片都退回估算值,总高度会跳一下。
 */
const measuredSizes = new Map<string, number>()
const sizeKey = (name: string) => `${props.sizeCacheKey}::${name}`

/*
 * 入场动画会给卡片挂 scale-85,getBoundingClientRect 量出来是缩小后的高度。
 * borderBoxSize / offsetHeight 都是布局尺寸,不受 transform 影响。
 */
const measureCardHeight = (element: Element, entry: ResizeObserverEntry | undefined) => {
  const box = entry?.borderBoxSize?.[0]

  return box ? Math.round(box.blockSize) : (element as HTMLElement).offsetHeight
}

const virtualizerOptions = computed(() => ({
  count: props.data.length,
  getScrollElement: () => props.scrollElement,
  getItemKey: (index: number) => props.data[index],
  estimateSize: (index: number) =>
    measuredSizes.get(sizeKey(props.data[index])) ?? props.estimateSize,
  measureElement: measureCardHeight,
  scrollMargin: props.scrollMargin,
  overscan: props.overscan,
  gap: props.gap,
}))

const columnRef = ref<HTMLDivElement>()
const rowVirtualizer = useVirtualizer(virtualizerOptions)

/*
 * 行里的折叠卡片展开 / 收起时，后面的行改用 transform 跟着挪，不走逐帧重排。
 * 动画收尾时它会回头调 measureElement，把卡片的新高度同步给 virtualizer。
 */
const virtualRowShift = createVirtualRowShift(
  () => columnRef.value,
  (row) => rowVirtualizer.value.measureElement(row),
)
provideVirtualRowShift(virtualRowShift)
const virtualRows = computed(() => rowVirtualizer.value.getVirtualItems())
const totalSize = computed(() => rowVirtualizer.value.getTotalSize())

/*
 * 每个元素只交给 virtualizer 量一次:
 *   - measureElement 每调一次都会读一次 offsetHeight,那是强制同步布局。ref 回调在每次
 *     patch 都会跑,滚动时就是每帧几十次强制布局,滚动的最差帧会明显变糟;
 *   - 第一次调用时 virtualizer 已经把元素挂进了自己的 ResizeObserver,之后卡片展开 /
 *     收起引起的高度变化由 observer 用 borderBoxSize 报回来,不需要再读一次布局。
 * 在 patch 里直接量还可能量到上一帧的样式,所以推到 nextTick。
 */
const measuredElements = new WeakSet<Element>()

const measureRow = (el: Element | null) => {
  if (!el || measuredElements.has(el)) return

  measuredElements.add(el)
  nextTick(() => {
    if (el.isConnected) {
      rowVirtualizer.value.measureElement(el)
    }
  })
}

/*
 * 页面恢复位置时不再依赖易漂移的 scrollTop,而是先按稳定的 item key 把锚点挂载出来,
 * 再由页面根据真实 DOM 偏移校准。校准也经由当前 virtualizer 下发,避免遗留一条尚未结束的
 * scrollToIndex 对账任务。
 */
const scrollToItem = (name: string) => {
  const index = props.data.indexOf(name)

  if (index < 0) return false

  rowVirtualizer.value.scrollToIndex(index, { align: 'start', behavior: 'auto' })

  return true
}

const correctScrollBy = (delta: number) => {
  const scrollElement = props.scrollElement

  if (!scrollElement) return

  rowVirtualizer.value.scrollToOffset(scrollElement.scrollTop + delta, { behavior: 'auto' })
}

defineExpose({ scrollToItem, correctScrollBy })

onBeforeUnmount(() => {
  virtualRowShift.destroy()

  for (const [key, size] of rowVirtualizer.value.itemSizeCache) {
    if (size > 0) {
      measuredSizes.set(sizeKey(String(key)), size)
    }
  }
})
</script>
