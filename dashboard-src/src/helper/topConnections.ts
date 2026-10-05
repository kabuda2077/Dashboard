import type { Connection } from '@/types'

// Fixed-capacity selection, stable for equal speeds; retain only the four visible identities.
export const selectTopDownloads = (
  connections: readonly Connection[],
  previous: readonly Connection[],
): Connection[] => {
  const top: Connection[] = []
  const retained = new Map<string, Connection>()
  const oldIds = new Set(previous.map((item) => item.id))
  for (const connection of connections) {
    if (oldIds.has(connection.id)) retained.set(connection.id, connection)
    if (!(connection.downloadSpeed > 0)) continue
    let position = 0
    while (position < top.length && top[position].downloadSpeed >= connection.downloadSpeed)
      position++
    if (position < 4) {
      top.splice(position, 0, connection)
      if (top.length > 4) top.pop()
    }
  }
  return Array.from({ length: 4 }, (_, index) => {
    if (top[index]) return { ...top[index] }
    const old = previous[index]
    return old ? { ...(retained.get(old.id) ?? old), downloadSpeed: 0 } : undefined
  }).filter((item): item is Connection => !!item)
}
