import { customBackgroundURL } from '@/store/settings'
import {
  deleteBase64FromIndexedDB,
  getBase64FromIndexedDB,
  LOCAL_IMAGE,
  saveBase64ToIndexedDB,
} from './indexeddb'
import { registerPersistenceFlusher } from './persistenceBarrier'

// One document-wide owner: closing/rebuilding a settings section must not let an
// old FileReader/decode overwrite a newer selection in another section instance.
let revision = 0
let active: AbortController | undefined
let tail: Promise<void> = Promise.resolve()
const enqueue = (
  work: (signal: AbortSignal, current: () => boolean, revision: number) => Promise<void>,
) => {
  active?.abort()
  const controller = new AbortController()
  active = controller
  const id = ++revision
  const current = () => id === revision && !controller.signal.aborted
  const result = tail
    .catch(() => {})
    .then(async () => {
      if (current()) await work(controller.signal, current, id)
    })
  tail = result
  const finished = () => {
    if (active === controller) active = undefined
  }
  void result.then(finished, finished)
  return result
}

registerPersistenceFlusher(async () => {
  for (;;) {
    const pending = tail
    try {
      await pending
    } catch (error) {
      if (pending === tail) throw error
    }
    if (pending === tail) return
  }
})
window.addEventListener('pagehide', () => active?.abort(), { once: true })

export const replaceBackgroundImage = (
  load: (signal: AbortSignal, current: () => boolean) => Promise<string>,
) =>
  enqueue(async (signal, current, id) => {
    const image = await load(signal, current)
    if (!current()) return
    const previous = await getBase64FromIndexedDB()
    if (!current()) return
    await saveBase64ToIndexedDB(image)
    if (!current()) {
      // A commit is short and non-cancellable. Restore it before allowing the next
      // queued selection/URL change to run, so a failed newer decode keeps old data.
      if (previous === undefined) await deleteBase64FromIndexedDB()
      else await saveBase64ToIndexedDB(previous)
      return
    }
    customBackgroundURL.value = `${LOCAL_IMAGE}-${Date.now()}-${id}`
  })

// Import/reset must quiesce a previous upload before replacing preference values.
export const cancelBackgroundUpdates = () => enqueue(async () => {})

export const replaceBackgroundUrl = (url: string) => {
  customBackgroundURL.value = url
  return enqueue(async (_, current) => {
    if (current() && !url.includes(LOCAL_IMAGE)) await deleteBase64FromIndexedDB()
  })
}
