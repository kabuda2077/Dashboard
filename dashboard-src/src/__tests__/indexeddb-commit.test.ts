import { afterEach, expect, it, vi } from 'vitest'

vi.mock('@/store/settings', () => ({ customBackgroundURL: { value: '' } }))
afterEach(() => vi.restoreAllMocks())
it('request success is not a commit; abort preserves cache and blocks native disposal until retry', async () => {
  vi.resetModules()
  const writes: IDBTransaction[] = []
  vi.spyOn(indexedDB, 'open').mockImplementation(() => {
    const db = {
      objectStoreNames: { contains: () => true },
      createObjectStore: vi.fn(),
      transaction: (_: string, mode: IDBTransactionMode) => {
        const tx = {
          error: null,
          oncomplete: null,
          onabort: null,
          onerror: null,
          objectStore: () => ({
            openCursor: () => {
              const cursor = request(null)
              queueMicrotask(() => tx.oncomplete?.call(tx, new Event('complete')))
              return cursor
            },
            put: () => request('background-image'),
            clear: () => request(undefined),
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
  const first = saveBase64ToIndexedDB('old').then(() => {
    settled = true
  })
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

it('waits for the initial read transaction and reopens after an abort following cursor success', async () => {
  vi.resetModules()
  const reads: IDBTransaction[] = []
  const close = vi.fn()
  const open = vi.spyOn(indexedDB, 'open').mockImplementation(
    () =>
      request({
        close,
        transaction: (_: string, mode: IDBTransactionMode) => {
          const tx = {
            oncomplete: null,
            onabort: null,
            onerror: null,
            objectStore: () => ({
              openCursor: () => request(null),
              put: () => {
                const result = request('background-image')
                queueMicrotask(() => tx.oncomplete?.call(tx, new Event('complete')))
                return result
              },
            }),
          } as unknown as IDBTransaction
          if (mode === 'readonly') reads.push(tx)
          return tx
        },
      }) as unknown as IDBOpenDBRequest,
  )
  open.mockClear()
  function request(result: unknown) {
    const value = { result, onsuccess: null as ((event: Event) => void) | null }
    queueMicrotask(() => value.onsuccess?.({ target: value } as unknown as Event))
    return value
  }
  const { getBase64FromIndexedDB, saveBase64ToIndexedDB } = await import('@/helper/indexeddb')
  let settled = false
  const reading = getBase64FromIndexedDB().then((value) => {
    settled = true
    return value
  })
  const rejected = expect(reading).rejects.toThrow('initialization aborted')
  await vi.waitFor(() => expect(reads).toHaveLength(1))
  expect(settled).toBe(false)
  reads[0].onabort?.call(reads[0], new Event('abort'))
  await rejected
  expect(close).toHaveBeenCalledOnce()
  const retry = getBase64FromIndexedDB()
  await vi.waitFor(() => expect(reads).toHaveLength(2))
  reads[1].oncomplete?.call(reads[1], new Event('complete'))
  expect(await retry).toBeUndefined()
  await saveBase64ToIndexedDB('recovered')
  expect(await getBase64FromIndexedDB()).toBe('recovered')
  expect(open).toHaveBeenCalledTimes(2)
})

it.each(['open', 'cursor'] as const)(
  'retries a failed database %s on a later read/write',
  async (failure) => {
    vi.resetModules()
    let attempts = 0
    const close = vi.fn()
    vi.spyOn(indexedDB, 'open').mockImplementation(() => {
      const fail = ++attempts === 1
      const database = {
        close,
        transaction: () => {
          const transaction = {
            oncomplete: null,
            objectStore: () => ({
              openCursor: () => {
                const cursor = request(null, fail && failure === 'cursor')
                if (!(fail && failure === 'cursor'))
                  queueMicrotask(() =>
                    transaction.oncomplete?.call(transaction, new Event('complete')),
                  )
                return cursor
              },
              put: () => {
                queueMicrotask(() =>
                  transaction.oncomplete?.call(transaction, new Event('complete')),
                )
                return request('background-image')
              },
            }),
          } as unknown as IDBTransaction
          return transaction
        },
      }
      return request(database, fail && failure === 'open') as unknown as IDBOpenDBRequest
    })
    function request(result: unknown, fail = false) {
      const value = {
        result,
        error: new Error('database unavailable'),
        onsuccess: null as ((event: Event) => void) | null,
        onerror: null as (() => void) | null,
      }
      queueMicrotask(() => {
        if (fail) value.onerror?.()
        else value.onsuccess?.({ target: value } as unknown as Event)
      })
      return value
    }
    const { saveBase64ToIndexedDB, getBase64FromIndexedDB } = await import('@/helper/indexeddb')
    const { flushLocalPersistence } = await import('@/helper/persistenceBarrier')
    await expect(saveBase64ToIndexedDB('failed')).rejects.toThrow('database unavailable')
    await expect(flushLocalPersistence()).rejects.toThrow('has not been saved')
    expect(await getBase64FromIndexedDB()).toBeUndefined()
    expect(attempts).toBe(2)
    if (failure === 'cursor') expect(close).toHaveBeenCalledOnce()
    await saveBase64ToIndexedDB('recovered')
    await expect(flushLocalPersistence()).resolves.toBeUndefined()
    expect(await getBase64FromIndexedDB()).toBe('recovered')
  },
)
