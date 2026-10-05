// Storage-neutral: importing the native flush bridge must not initialize stores.
// Owners register after their dependencies, so flush the owners first (history
// snapshot -> IndexedDB commit) and acknowledge native disposal only afterwards.
const flushers: (() => Promise<void>)[] = []
export const registerPersistenceFlusher = (flush: () => Promise<void>) => {
  flushers.unshift(flush)
}
export const flushLocalPersistence = async () => {
  for (const flush of flushers) await flush()
}
