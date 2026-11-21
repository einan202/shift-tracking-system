import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { expect, it } from 'vitest'

// Frontend guard for the clock rule: the browser clock must never be used for attendance.
// Forbids reading the current time (Date.now(), new Date() without arguments, performance.now()) anywhere in the app.
// `new Date(<server value>)` is allowed because it only parses a timestamp that came from the API.
const FORBIDDEN = /Date\.now\s*\(|new Date\(\s*\)|performance\.now\s*\(|\bnew Intl\.DateTimeFormat\([^)]*\)\.format\(\s*\)/

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name)
    if (statSync(p).isDirectory()) return sources(p)
    return /\.(ts|tsx)$/.test(name) && !/\.test\.tsx?$/.test(name) ? [p] : []
  })
}

it('no app source reads the browser clock', () => {
  const offenders = sources(join(__dirname, '../..')).filter((f) => FORBIDDEN.test(readFileSync(f, 'utf8')))
  expect(offenders).toEqual([])
})

it('the guard itself detects forbidden usage', () => {
  expect(FORBIDDEN.test('const t = Date.now()')).toBe(true)
  expect(FORBIDDEN.test('const d = new Date()')).toBe(true)
  expect(FORBIDDEN.test('const d = new Date(iso)')).toBe(false)
})
