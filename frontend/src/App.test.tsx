import { render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => vi.unstubAllGlobals())

it('shows backend status ok when /api/health succeeds', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true }))
  render(<App />)
  expect(await screen.findByText('Backend status: ok')).toBeTruthy()
})

it('shows backend status error when /api/health fails', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('down')))
  render(<App />)
  expect(await screen.findByText('Backend status: error')).toBeTruthy()
})
