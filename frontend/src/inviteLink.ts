// Invite / reset links are /set-password#token=...  The token is in the URL FRAGMENT, which browsers never send
// to the server, so it stays out of access logs. It is read once and removed from the address bar so it does not
// linger in the visible URL, history entries or screenshots. Legacy links (?token=...) still work, once.

let cached: string | null | undefined

export function takeInviteToken(): string | null {
  if (cached !== undefined) return cached // survives React StrictMode's double render and re-renders after the URL was cleaned

  const fromHash = new URLSearchParams(window.location.hash.replace(/^#/, '')).get('token')
  const fromQuery = new URLSearchParams(window.location.search).get('token')
  cached = fromHash ?? fromQuery

  if (fromHash !== null || fromQuery !== null) {
    window.history.replaceState(null, '', window.location.pathname)
  }
  return cached
}

/** Test helper: forget the remembered token. */
export function resetInviteTokenForTests() {
  cached = undefined
}
