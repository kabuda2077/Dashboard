import type { Connection } from '@/types'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ref } from 'vue'

vi.mock('@/store/setup', () => ({ activeBackend: ref({ uuid: 'A' }) }))
vi.mock('@/helper', () => ({
  getConnectionChains: () => ['DIRECT'],
  getConnectionDownload: (connection: Connection) => connection.download,
  getConnectionUpload: (connection: Connection) => connection.upload,
  getConnectionHostname: () => 'example.test',
  getConnectionSourceIP: () => '192.0.2.1',
  getProcessFromConnection: () => 'example.exe',
}))
vi.mock('@/store/settings', () => ({ customBackgroundURL: ref('') }))

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  vi.spyOn(console, 'error').mockImplementation(() => {})
})
afterEach(() => {
  vi.clearAllTimers()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

const batch = (count: number) =>
  Array.from(
    { length: count },
    (_, id) => ({ id: String(id), download: 1, upload: 1 }) as Connection,
  )

const fixture = async () => {
  const disk = new Map<string, string>()
  let failA = false
  let holdA = false
  let release: (() => void) | undefined
  vi.spyOn(indexedDB, 'open').mockImplementation((name) => {
    const database = {
      objectStoreNames: { contains: () => true },
      transaction: () => {
        const transaction = {
          error: null,
          oncomplete: null,
          onabort: null,
          onerror: null,
          objectStore: () => ({
            openCursor: () => request(null),
            put: ({ key, value }: { key: string; value: string }) => {
              const result = request(key)
              const finish = () => {
                if (name === 'connection-history' && key.startsWith('A-') && failA) {
                  transaction.onabort?.call(transaction, new Event('abort'))
                } else {
                  disk.set(`${name}:${key}`, value)
                  transaction.oncomplete?.call(transaction, new Event('complete'))
                }
              }
              if (key.startsWith('A-') && holdA) {
                holdA = false
                release = finish
              } else queueMicrotask(finish)
              return result
            },
            clear: () => {
              queueMicrotask(() => {
                for (const key of disk.keys()) if (key.startsWith(`${name}:`)) disk.delete(key)
                transaction.oncomplete?.call(transaction, new Event('complete'))
              })
              return request(undefined)
            },
          }),
        } as unknown as IDBTransaction
        return transaction
      },
    }
    return request(database) as unknown as IDBOpenDBRequest
  })
  function request(result: unknown) {
    const value = { result, onsuccess: null as ((event: Event) => void) | null, onerror: null }
    queueMicrotask(() => value.onsuccess?.({ target: value } as unknown as Event))
    return value
  }
  const history = await import('@/store/conn-history')
  const { activeBackend } = await import('@/store/setup')
  const { flushLocalPersistence } = await import('@/helper/persistenceBarrier')
  const { ConnectionHistoryType } = await import('@/helper/indexeddb')
  const count = () => JSON.parse(disk.get('connection-history:A-sourceIP') ?? '[]')[0]?.count
  const currentCount = () =>
    history.aggregatedDataMap.value[ConnectionHistoryType.SourceIP][0]?.count
  const switchTo = async (uuid: string) => {
    activeBackend.value = { uuid } as typeof activeBackend.value
    await history.initAggregatedDataMap()
  }
  await switchTo('A')
  history.saveConnectionHistory(batch(100))
  await flushLocalPersistence()
  expect(count()).toBe(100)
  return {
    history,
    flushLocalPersistence,
    count,
    currentCount,
    switchTo,
    fail: (value: boolean) => {
      failA = value
    },
    hold: () => {
      holdA = true
    },
    release: () => {
      expect(release).toBeDefined()
      const finish = release!
      release = undefined
      finish()
    },
    isHeld: () => !!release,
  }
}

it('retains 100 + 20 across a failed old-session save and returns to save 125 truthfully', async () => {
  const context = await fixture()
  context.history.saveConnectionHistory(batch(20))
  context.fail(true)
  await context.switchTo('B')
  expect(context.count()).toBe(100)
  await expect(context.flushLocalPersistence()).rejects.toThrow(/has not been saved/)
  await context.switchTo('A')
  expect(context.currentCount()).toBe(120)
  context.history.saveConnectionHistory(batch(5))
  context.fail(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBe(125)
  await context.switchTo('B')
  await context.switchTo('A')
  expect(context.currentCount()).toBe(125)
  await expect(context.flushLocalPersistence()).resolves.toBeUndefined()
})

it.each([false, true])(
  'an in-flight retry of 120 cannot overwrite the newer 125 snapshot (old commit fails: %s)',
  async (oldFails) => {
    const context = await fixture()
    context.history.saveConnectionHistory(batch(20))
    context.fail(true)
    await context.switchTo('B')
    await context.switchTo('A')
    context.fail(false)
    context.hold()
    const retry = context.flushLocalPersistence()
    await vi.waitFor(() => expect(context.isHeld()).toBe(true))
    context.history.saveConnectionHistory(batch(5))
    const newer = context.flushLocalPersistence()
    context.fail(oldFails)
    context.release()
    context.fail(false)
    await Promise.all([retry, newer])
    expect(context.count()).toBe(125)
    await expect(context.flushLocalPersistence()).resolves.toBeUndefined()
    expect(context.count()).toBe(125)
  },
)

it('retries a failed departed session while another session stays active', async () => {
  const context = await fixture()
  context.history.saveConnectionHistory(batch(20))
  context.fail(true)
  await context.switchTo('B')
  await expect(context.flushLocalPersistence()).rejects.toThrow(/has not been saved/)
  context.fail(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBe(120)
  await context.switchTo('A')
  expect(context.currentCount()).toBe(120)
})

it('an explicit clear cannot resurrect a retained failed snapshot on a later flush', async () => {
  const context = await fixture()
  context.history.saveConnectionHistory(batch(20))
  context.fail(true)
  await context.switchTo('B')
  await context.history.clearConnectionHistory()
  context.fail(false)
  await context.flushLocalPersistence()
  expect(context.count()).toBeUndefined()
  await context.switchTo('A')
  expect(context.currentCount()).toBeUndefined()
})
