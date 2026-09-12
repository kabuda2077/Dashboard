import type { StorageLike, UseStorageOptions } from '@vueuse/core'
import { useStorage as useVueUseStorage } from '@vueuse/core'
import type { MaybeRefOrGetter } from 'vue'

/**
 * vueuse 的 useStorage 默认 writeDefaults: true，初始化时会把默认值立刻写入
 * localStorage —— 哪怕用户从没改过这一项。这会导致：
 * 1. 导出配置(仅收集 config/ 开头的 key)把所有默认值也导出，无法区分用户真正改过什么；
 * 2. 以后调整某项的默认值时，老用户 localStorage 里已经落了旧默认值，新默认不会生效。
 *
 * 这里统一关闭 writeDefaults：只有用户实际修改过的项才会写入 storage，未修改的项
 * 始终读取代码里的默认值。
 */
export function useStorage<T>(
  key: MaybeRefOrGetter<string>,
  defaults: MaybeRefOrGetter<T>,
  storage?: StorageLike,
  options?: UseStorageOptions<T>,
) {
  return useVueUseStorage(key, defaults, storage, {
    writeDefaults: false,
    ...options,
  })
}
