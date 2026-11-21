import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { NetworkError } from '../../api'
import ClockPanel from './ClockPanel'
import type { ClockApi, StatusBody } from './punch'

const ok = (data: StatusBody) => ({ ok: true, status: 200, data })
const none: StatusBody = { clockedIn: false, record: null }
const open: StatusBody = { clockedIn: true, record: { id: 1, clockInUtc: '2026-02-02T08:00:00Z', clockOutUtc: null, workDate: '2026-02-02', provider: 'x' } }

it('after a dropped connection it checks /status and shows that the punch was recorded', async () => {
  let recorded = false
  const client: ClockApi = {
    punch: async () => { recorded = true; throw new NetworkError('dropped') }, // server saved it, response lost
    status: async () => ok(recorded ? open : none),
  }
  render(<ClockPanel client={client} />)

  fireEvent.click(await screen.findByRole('button', { name: 'Clock in' }))

  expect(await screen.findByText(/your punch was recorded/)).toBeTruthy()
  expect(await screen.findByRole('button', { name: 'Clock out' })).toBeTruthy() // UI now reflects the server state
})

it('disables the button while a request is in flight', async () => {
  let release: (v: ReturnType<typeof ok>) => void = () => {}
  const client: ClockApi = { punch: () => new Promise((r) => { release = r }), status: async () => ok(none) }
  render(<ClockPanel client={client} />)

  fireEvent.click(await screen.findByRole('button', { name: 'Clock in' }))
  const busy = await screen.findByRole('button', { name: /Contacting time service/ })
  expect((busy as HTMLButtonElement).disabled).toBe(true)

  release(ok(open))
  expect(await screen.findByRole('button', { name: 'Clock out' })).toBeTruthy()
})

it('shows that nothing was recorded when the time service is down (503)', async () => {
  const client: ClockApi = {
    punch: async () => ({ ok: false, status: 503, data: null }),
    status: async () => ok(none),
  }
  render(<ClockPanel client={client} />)

  fireEvent.click(await screen.findByRole('button', { name: 'Clock in' }))

  expect(await screen.findByText(/Nothing was recorded/i)).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Clock in' })).toBeTruthy()
})
