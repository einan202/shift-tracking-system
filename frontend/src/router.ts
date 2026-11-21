import { useSyncExternalStore } from 'react'

// Tiny path-based router (no dependency): every section has its own URL, so reload, deep links and the
// browser Back/Forward buttons work. nginx (and the Vite dev server) already serve index.html for any path.

const subscribe = (notify: () => void) => {
  window.addEventListener('popstate', notify)
  return () => window.removeEventListener('popstate', notify)
}

/** The current URL path, re-read on every history change. */
export const usePath = () => useSyncExternalStore(subscribe, () => window.location.pathname)

/** Go to a path. `replace` swaps the current history entry (used for redirects, so Back does not loop). */
export function navigate(to: string, replace = false) {
  if (window.location.pathname === to) return
  if (replace) window.history.replaceState(null, '', to)
  else window.history.pushState(null, '', to)
  window.dispatchEvent(new PopStateEvent('popstate')) // pushState/replaceState do not fire it themselves
}
