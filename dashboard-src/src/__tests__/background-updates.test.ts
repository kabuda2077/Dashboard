import { afterEach, expect, it, vi } from 'vitest'
import { ref } from 'vue'
vi.mock('@/store/settings', () => ({ customBackgroundURL: ref('') }))
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  vi.restoreAllMocks()
})
it('flush waits for decoding and the latest selection wins even if an old loader ignores cancellation', async () => {
  vi.resetModules()
  localStorage.clear()
  const { replaceBackgroundImage } = await import('@/helper/backgroundUpdates')
  const { getBase64FromIndexedDB } = await import('@/helper/indexeddb')
  const { flushLocalPersistence } = await import('@/helper/persistenceBarrier')
  let decode!: (image: string) => void
  const old = replaceBackgroundImage(
    () =>
      new Promise((resolve) => {
        decode = resolve
      }),
  )
  await vi.waitFor(() => expect(decode).toBeTypeOf('function'))
  let flushed = false
  const flush = flushLocalPersistence().then(() => {
    flushed = true
  })
  await Promise.resolve()
  expect(flushed).toBe(false)
  const latest = replaceBackgroundImage(async () => 'B')
  decode('A')
  await Promise.all([old, latest, flush])
  expect(await getBase64FromIndexedDB()).toBe('B')
  expect(flushed).toBe(true)
})
it('a superseded commit is restored before a newer failed decode, and a URL change can recover', async () => {
  vi.resetModules()
  localStorage.clear()
  const db = await import('@/helper/indexeddb')
  await db.saveBase64ToIndexedDB('original')
  const save = db.saveBase64ToIndexedDB
  let committed = false
  let finish!: () => void
  const commit = new Promise<void>((resolve) => {
    finish = resolve
  })
  vi.spyOn(db, 'saveBase64ToIndexedDB').mockImplementation(async (value) => {
    const key = await save(value)
    if (value === 'A') {
      committed = true
      await commit
    }
    return key
  })
  const { replaceBackgroundImage, replaceBackgroundUrl } =
    await import('@/helper/backgroundUpdates')
  const { flushLocalPersistence } = await import('@/helper/persistenceBarrier')
  const first = replaceBackgroundImage(async () => 'A')
  await vi.waitFor(() => expect(committed).toBe(true))
  const second = replaceBackgroundImage(async () => {
    throw new Error('new decode failed')
  })
  const failure = expect(second).rejects.toThrow('new decode failed')
  finish()
  await first
  await failure
  expect(await db.getBase64FromIndexedDB()).toBe('original')
  await expect(flushLocalPersistence()).rejects.toThrow('new decode failed')
  await replaceBackgroundUrl('')
  await expect(flushLocalPersistence()).resolves.toBeUndefined()
  expect(await db.getBase64FromIndexedDB()).toBeUndefined()
})
it('clearing the URL cancels an unfinished upload rather than allowing it to resurrect the image', async () => {
  vi.resetModules()
  localStorage.clear()
  const { replaceBackgroundImage, replaceBackgroundUrl } =
    await import('@/helper/backgroundUpdates')
  const { getBase64FromIndexedDB } = await import('@/helper/indexeddb')
  const { customBackgroundURL } = await import('@/store/settings')
  let finish!: (value: string) => void
  const upload = replaceBackgroundImage(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  await vi.waitFor(() => expect(finish).toBeTypeOf('function'))
  const clear = replaceBackgroundUrl('')
  finish('obsolete')
  await Promise.all([upload, clear])
  expect(customBackgroundURL.value).toBe('')
  expect(await getBase64FromIndexedDB()).toBeUndefined()
})
