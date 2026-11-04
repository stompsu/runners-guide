import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, XAxis, YAxis } from 'recharts'

// Presentation-only series: callers select and format server values; no domain aggregation here.
export type ChartPoint = { label: string; value: number | null }
export type SeriesPoint = { label: string } & Record<string, number | string | null>
export type SeriesStyle = { key: string; name: string; dashed?: boolean }

// Same-unit series sharing one axis. Series differ by dash pattern and a visible legend, not only
// colour; exact values are always available in the accompanying table.
export function SeriesChart({ points, series, label, unit }: {
  points: SeriesPoint[]; series: SeriesStyle[]; label: string; unit: string
}) {
  const colours = ['#21573b', '#8a4b0f']
  return <div className="insights-chart" role="img" aria-label={label}>
    <div aria-hidden="true">
      <ResponsiveContainer width="100%" height={260} minWidth={0}>
        <LineChart data={points} accessibilityLayer={false}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis dataKey="label" tick={{ fontSize: 11 }} minTickGap={30} />
          <YAxis width={65} tick={{ fontSize: 11 }} label={{ value: unit, angle: -90, position: 'insideLeft' }} />
          <Legend />
          {series.map((item, index) => <Line key={item.key} dataKey={item.key} name={item.name} type="linear"
            stroke={colours[index % colours.length]} strokeWidth={2}
            strokeDasharray={item.dashed ? '6 4' : undefined} legendType={item.dashed ? 'plainline' : 'line'}
            dot={false} connectNulls={false} isAnimationActive={false} />)}
        </LineChart>
      </ResponsiveContainer>
    </div>
  </div>
}

export function Chart({ points, label, unit, kind = 'bar' }: {
  points: ChartPoint[]; label: string; unit: string; kind?: 'bar' | 'line'
}) {
  const axes = <>
    <CartesianGrid strokeDasharray="3 3" />
    <XAxis dataKey="label" tick={{ fontSize: 11 }} minTickGap={30} />
    <YAxis width={65} tick={{ fontSize: 11 }} label={{ value: unit, angle: -90, position: 'insideLeft' }} />
  </>
  return <div className="insights-chart" role="img" aria-label={label}>
    <div aria-hidden="true">
      <ResponsiveContainer width="100%" height={240} minWidth={0}>
        {kind === 'line'
          ? <LineChart data={points} accessibilityLayer={false}>{axes}
              <Line dataKey="value" type="linear" stroke="#21573b" strokeWidth={2} dot={{ r: 4 }} connectNulls={false} isAnimationActive={false} />
            </LineChart>
          : <BarChart data={points} accessibilityLayer={false}>{axes}
              <Bar dataKey="value" fill="#39704e" isAnimationActive={false} />
            </BarChart>}
      </ResponsiveContainer>
    </div>
  </div>
}
