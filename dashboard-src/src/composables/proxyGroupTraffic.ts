/*
 * 每个代理组的下行速率。
 *
 * 原来每张组卡片各自遍历一遍全部活跃连接(O(组数 × 连接数)),连接 WS 每秒推一拍,
 * 组一多主线程就一直在做这件重复的事。改成一拍只按连接链路累加一次,卡片查表即可。
 */
import { getConnectionChains } from '@/helper'
import { activeConnections } from '@/store/connections'
import { computed } from 'vue'

export const downloadSpeedByProxyGroup = computed(() => {
  const speedMap = new Map<string, number>()

  for (const connection of activeConnections.value) {
    const speed = connection.downloadSpeed

    if (!speed) continue

    const chains = getConnectionChains(connection)

    for (let i = 0; i < chains.length; i++) {
      const name = chains[i]

      // 链路里同名节点只算一次,与过去「chains.includes(name) 就整条计入」的口径一致
      if (chains.indexOf(name) !== i) continue

      speedMap.set(name, (speedMap.get(name) ?? 0) + speed)
    }
  }

  return speedMap
})

export const getDownloadSpeedByProxyGroup = (name: string) =>
  downloadSpeedByProxyGroup.value.get(name) ?? 0
