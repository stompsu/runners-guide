import { cloneElement, type ReactElement } from 'react'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

// Give the real Recharts graph a deterministic layout in jsdom (which has no CSS layout).
vi.mock('recharts', async importOriginal => {
  const actual = await importOriginal<typeof import('recharts')>()
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactElement<{ width: number; height: number }> }) =>
      cloneElement(children, { width: 600, height: 240 }),
  }
})
import { Chart, SeriesChart } from './Charts'
afterEach(cleanup)

describe('lazy chart presentation', () => {
  it('labels the chart, hides redundant SVG semantics and needs no hover for information', () => {
    const { container } = render(<Chart label="Running distance. Exact values in table below." unit="km"
      points={[{ label: '2026-10-01', value: 12.555 }, { label: '2026-10-08', value: 0 }]} />)
    expect(screen.getByRole('img', { name: /Running distance/ })).toBeVisible()
    expect(container.querySelector('svg')?.closest('[aria-hidden=true]')).not.toBeNull()
    // Zero retains its axis bucket but has no rectangle with non-zero height.
    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(1)
    expect(container).toHaveTextContent('2026-10-08')
    expect(container.querySelector('.recharts-tooltip-wrapper')).toBeNull()
  })
  it('renders only the API-provided pace points, leaving unavailable values as gaps', () => {
    const { container } = render(<Chart label="Easy pace" unit="s/km" kind="line"
      points={[{ label: 'A', value: 337.123 }, { label: 'B', value: null }, { label: 'C', value: 320 }]} />)
    expect(container.querySelectorAll('.recharts-line-dot')).toHaveLength(2)
    expect(screen.getByRole('img', { name: 'Easy pace' })).toBeVisible()
  })
  it('draws acute and chronic as distinguishable same-axis series with a visible legend and null gaps', () => {
    const { container } = render(<SeriesChart label="Daily load" unit="min × RPE"
      series={[{ key: 'acute', name: 'Acute' }, { key: 'chronic', name: 'Chronic / week', dashed: true }]}
      points={[{ label: 'A', acute: 10, chronic: 8 }, { label: 'B', acute: 12, chronic: null }, { label: 'C', acute: 9, chronic: 9 }]} />)
    expect(screen.getByRole('img', { name: 'Daily load' })).toBeVisible()
    const lines = container.querySelectorAll('.recharts-line-curve')
    expect(lines).toHaveLength(2)
    expect(lines[1].getAttribute('stroke-dasharray')).toBe('6 4')
    expect(lines[0].getAttribute('stroke-dasharray')).toBeNull()
    expect(container.querySelector('.recharts-legend-wrapper')).toHaveTextContent('AcuteChronic / week')
    expect(container.querySelectorAll('.recharts-yAxis')).toHaveLength(1)
  })
})
