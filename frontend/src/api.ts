// Thin fetch wrapper. All times shown in the UI come from API responses; this app never reads the
// browser clock for attendance (see the source-scan test in features/attendance/noBrowserClock.test.ts).

export type ApiResult<T> = { ok: boolean; status: number; data: T | null }

/** The request may or may not have reached the server (offline, timeout, dropped connection). */
export class NetworkError extends Error {}

const TOKEN_KEY = 'clockapp.token'
let unauthorizedHandler: (() => void) | null = null

export const tokenStore = {
  get: () => sessionStorage.getItem(TOKEN_KEY),
  set: (t: string) => sessionStorage.setItem(TOKEN_KEY, t),
  clear: () => sessionStorage.removeItem(TOKEN_KEY),
}

/** AuthProvider registers this so any 401 on an authenticated call logs the user out. */
export function onUnauthorized(handler: (() => void) | null) {
  unauthorizedHandler = handler
}

const TIMEOUT_MS = 10_000

export async function api<T = unknown>(path: string, init: { method?: string; body?: unknown } = {}): Promise<ApiResult<T>> {
  const token = tokenStore.get()
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS)
  try {
    const res = await fetch(`/api${path}`, {
      method: init.method ?? (init.body === undefined ? 'GET' : 'POST'),
      headers: {
        ...(init.body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      signal: controller.signal,
    })
    let data: T | null = null
    try {
      data = (await res.json()) as T
    } catch {
      /* empty or non-JSON body */
    }
    if (res.status === 401 && token) unauthorizedHandler?.()
    return { ok: res.ok, status: res.status, data }
  } catch {
    throw new NetworkError('Network error or timeout')
  } finally {
    clearTimeout(timer)
  }
}

export const errorText = (r: ApiResult<unknown>, fallback = 'Something went wrong.') =>
  (r.data as { error?: string } | null)?.error ?? fallback
