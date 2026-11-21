import { beforeEach, expect, it } from 'vitest'
import { resetInviteTokenForTests, takeInviteToken } from './inviteLink'

beforeEach(() => {
  resetInviteTokenForTests()
  window.history.replaceState(null, '', '/')
})

it('reads the token from the URL fragment and removes it from the address bar', () => {
  window.history.replaceState(null, '', '/set-password#token=abc123')

  expect(takeInviteToken()).toBe('abc123')
  expect(window.location.hash).toBe('')
  expect(window.location.search).toBe('')
  expect(window.location.pathname).toBe('/set-password')
})

it('keeps returning the token after the URL was cleaned (re-renders, StrictMode)', () => {
  window.history.replaceState(null, '', '/set-password#token=abc123')

  expect(takeInviteToken()).toBe('abc123')
  expect(takeInviteToken()).toBe('abc123')
})

it('still accepts a legacy ?token= link once, and cleans it up', () => {
  window.history.replaceState(null, '', '/set-password?token=legacy')

  expect(takeInviteToken()).toBe('legacy')
  expect(window.location.search).toBe('')
})

it('returns null when there is no token', () => {
  window.history.replaceState(null, '', '/set-password')

  expect(takeInviteToken()).toBeNull()
})
