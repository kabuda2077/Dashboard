import { afterEach, expect, it, vi } from 'vitest'

vi.mock('@/store/settings', () => ({ customBackgroundURL: { value: '' } }))
afterEach(() => vi.restoreAllMocks())
it('request success is not a commit; abort preserves cache and blocks native disposal until retry', async () => {
  vi.resetModules()
  const writes: IDBTransaction[] = []
  vi.spyOn(indexedDB, 'open').mockImplementation((name) => {
    const db = {
      objectStoreNames: { contains: () => true }, createObjectStore: vi.fn(),
      transaction: (_: string, mode: IDBTransactionMode) => {
        const tx = { error: null, oncomplete: null, onabort: null, onerror: null,
          objectStore: () => ({
            openCursor: () => request(null),
            put: () => request('background-image'), clear: () => request(undefined),
          }),
        } as unknown as IDBTransaction
        if (mode === 'readwrite') writes.push(tx)
        return tx
      },
    }
    return request(db) as unknown as IDBOpenDBRequest
  })
  function request(result: unknown) {
    const value = { result, onsuccess: null as ((event: Event) => void) | null, onerror: null }
    queueMicrotask(() => value.onsuccess?.({ target: value } as unknown as Event))
    return value
  }
  const { saveBase64ToIndexedDB, getBase64FromIndexedDB } = await import('@/helper/indexeddb')
  const { flushLocalPersistence } = await import('@/helper/persistenceBarrier')
  let settled = false
  const first = saveBase64ToIndexedDB('old').then(() => { settled = true })
  await vi.waitFor(() => expect(writes).toHaveLength(1))
  expect(settled).toBe(false)
  expect(await getBase64FromIndexedDB()).toBeUndefined()
  writes[0].oncomplete?.call(writes[0], new Event('complete'))
  await first
  expect(await getBase64FromIndexedDB()).toBe('old')
  const failed = saveBase64ToIndexedDB('not-committed')
  const failure = expect(failed).rejects.toThrow('aborted')
  await vi.waitFor(() => expect(writes).toHaveLength(2))
  writes[1].onabort?.call(writes[1], new Event('abort'))
  await failure
  expect(await getBase64FromIndexedDB()).toBe('old')
  await expect(flushLocalPersistence()).rejects.toThrow('has not been saved')
  const retry = saveBase64ToIndexedDB('new')
  await vi.waitFor(() => expect(writes).toHaveLength(3))
  writes[2].oncomplete?.call(writes[2], new Event('complete'))
  await retry
  await expect(flushLocalPersistence()).resolves.toBeUndefined()
  expect(await getBase64FromIndexedDB()).toBe('new')
})
