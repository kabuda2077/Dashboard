import { vi } from 'vitest'

// Use Happy DOM's Storage instances so VueUse follows its native storage-event
// path, including same-document writes and cross-document event handling.
// Plain object mocks incorrectly select VueUse's custom-backend event path.
const localStorageMock = new Storage()
const sessionStorageMock = new Storage()

Object.defineProperty(window, 'localStorage', {
  configurable: true,
  value: localStorageMock,
})
Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: localStorageMock,
})
Object.defineProperty(window, 'sessionStorage', {
  configurable: true,
  value: sessionStorageMock,
})
Object.defineProperty(globalThis, 'sessionStorage', {
  configurable: true,
  value: sessionStorageMock,
})

Object.defineProperty(window, 'matchMedia', {
  configurable: true,
  writable: true,
  value: vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
})

Object.defineProperty(navigator, 'standalone', {
  configurable: true,
  value: false,
})

const createSuccessRequest = <T>(result: T): IDBRequest<T> => {
  const request = {
    result,
    error: null,
    onsuccess: null as ((event: Event) => void) | null,
    onerror: null as ((event: Event) => void) | null,
  }
  queueMicrotask(() => {
    request.onsuccess?.({ target: request } as unknown as Event)
  })
  return request as IDBRequest<T>
}

const createDatabase = (name: string): IDBDatabase => {
  const db = {
    name,
    objectStoreNames: {
      contains: () => true,
    },
    createObjectStore: vi.fn(),
    transaction: () => ({
      objectStore: () => ({
        openCursor: () => createSuccessRequest<IDBCursorWithValue | null>(null),
        put: () => createSuccessRequest(undefined),
        clear: () => createSuccessRequest(undefined),
        delete: () => createSuccessRequest(undefined),
      }),
    }),
  }
  return db as unknown as IDBDatabase
}

const indexedDBMock = {
  open: vi.fn((name: string) => {
    const request = {
      result: createDatabase(name),
      error: null,
      onsuccess: null as ((event: Event) => void) | null,
      onerror: null as ((event: Event) => void) | null,
      onupgradeneeded: null as ((event: IDBVersionChangeEvent) => void) | null,
    }
    queueMicrotask(() => {
      request.onupgradeneeded?.({ target: request } as unknown as IDBVersionChangeEvent)
      request.onsuccess?.({ target: request } as unknown as Event)
    })
    return request as IDBOpenDBRequest
  }),
}

Object.defineProperty(window, 'indexedDB', {
  configurable: true,
  value: indexedDBMock,
})

Object.defineProperty(globalThis, 'indexedDB', {
  configurable: true,
  value: indexedDBMock,
})
