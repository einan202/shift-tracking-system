import type { MouseEvent } from 'react'
import { navigate } from '../router'
import type { Route } from '../routes'

/** Section navigation: real links (open in a new tab, copy, Back/Forward work); the current one is marked with aria-current. */
export default function NavTabs({ routes, current }: { routes: Route[]; current: string }) {
  const go = (e: MouseEvent<HTMLAnchorElement>, path: string) => {
    // Let the browser handle ctrl/cmd/shift/middle clicks (open in a new tab/window).
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return
    e.preventDefault()
    navigate(path)
  }
  return (
    <nav aria-label="Sections" className="tabs">
      {routes.map((r) => (
        <a key={r.path} href={r.path} className="tabs__tab" aria-current={r.path === current ? 'page' : undefined} onClick={(e) => go(e, r.path)}>
          {r.label}
        </a>
      ))}
    </nav>
  )
}
