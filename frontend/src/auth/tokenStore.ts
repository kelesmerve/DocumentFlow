const storageKey = 'documentflow.access-token'
const changedEvent = 'documentflow:auth-changed'
let memoryToken: string | null = null

function notify(): void {
  if (typeof window !== 'undefined') window.dispatchEvent(new Event(changedEvent))
}

export const tokenStore = {
  get(): string | null {
    if (memoryToken) return memoryToken
    if (typeof window === 'undefined') return null
    memoryToken = window.localStorage.getItem(storageKey)
    return memoryToken
  },
  set(token: string): void {
    memoryToken = token
    window.localStorage.setItem(storageKey, token)
    notify()
  },
  clear(): void {
    memoryToken = null
    if (typeof window !== 'undefined') window.localStorage.removeItem(storageKey)
    notify()
  },
  subscribe(listener: () => void): () => void {
    window.addEventListener(changedEvent, listener)
    return () => window.removeEventListener(changedEvent, listener)
  },
}
