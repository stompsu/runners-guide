import { lazy, Suspense, type ReactNode } from 'react'
import type { components } from '../api/generated/openapi'
import { en } from '../i18n'
import { coverage, derived, finite, metric, number, pace, reason, scaled, unitLabel } from './format'
import { typeName } from './typeName'
import type { ChartPoint, SeriesPoint, SeriesStyle } from './Charts'

// Recharts lives only in the lazy Charts chunk, loaded when the dashboard renders a chart.
const Chart = lazy(() => import('./Charts').then(module => ({ default: module.Chart })))
const SeriesChart = lazy(() => import('./Charts').then(module => ({ default: module.SeriesChart })))
type Schemas = components['schemas']
const t = en.insights
const totalsMetrics = [
  ['runningDistanceMeters', t.distance, 'km'],
  ['durationSeconds', t.duration, 'minutes'],
  ['elevationGainMeters', t.gain, 'meters'],
  ['elevationLossMeters', t.loss, 'meters'],
  ['load', t.load, 'load'],
] as const
const comparisonMetrics = totalsMetrics.filter(item => item[0] !== 'elevationLossMeters')

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return <section className="insights-section" aria-label={title}><h2>{title}</h2>{children}</section>
}

export function Table({ caption, headers, children }: { caption: string; headers: string[]; children: ReactNode }) {
  return <div className="insights-table-scroll" tabIndex={0} role="region" aria-label={`${caption} — ${t.scrollable}`}>
    <table><caption>{caption}</caption><thead><tr>{headers.map(header => <th key={header} scope="col">{header}</th>)}</tr></thead>
      <tbody>{children}</tbody>
    </table>
  </div>
}

function Plot({ title, points, unit, kind }: { title: string; points: ChartPoint[]; unit: string; kind?: 'bar' | 'line' }) {
  return <Suspense fallback={<p role="status">{t.chartLoading}</p>}>
    <Chart label={`${title}. ${t.chartHelp}`} points={points} unit={unit} kind={kind} />
  </Suspense>
}

function SeriesPlot({ title, points, series, unit }: { title: string; points: SeriesPoint[]; series: SeriesStyle[]; unit: string }) {
  return <Suspense fallback={<p role="status">{t.chartLoading}</p>}>
    <SeriesChart label={`${title}. ${t.chartHelp}`} points={points} series={series} unit={unit} />
  </Suspense>
}

function Totals({ title, period }: { title: string; period: Schemas['InsightPeriod'] }) {
  return <Section title={title}>
    <p>{period.from} – {period.to} · {t.sessions}: <strong>{number(period.totals.sessionCount, 0)}</strong></p>
    <p>{t.restSessions}: <strong>{number(period.totals.restSessionCount, 0)}</strong> · {t.restHelp}</p>
    <p className="muted">{t.coverageHelp}</p>
    <Table caption={title} headers={[t.metric, t.value, t.coverage]}>
      {totalsMetrics.map(([key, label, unit]) => <tr key={key}>
        <th scope="row">{label}</th><td>{metric(period.totals[key].knownSubtotal, unit)}</td><td>{coverage(period.totals[key])}</td>
      </tr>)}
    </Table>
  </Section>
}

function Comparison({ title, value }: { title: string; value: Schemas['PeriodComparison'] }) {
  return <div>
    <h3>{title}</h3>
    <p className="muted">{t.current}: {value.currentFrom} – {value.currentTo}<br />{t.previous}: {value.previousFrom} – {value.previousTo}</p>
    <Table caption={title} headers={[t.metric, t.current, t.previous, t.change]}>
      {comparisonMetrics.map(([key, label, unit]) => {
        const comparison = value[key]
        return <tr key={key}><th scope="row">{label}</th>
          <td>{metric(comparison.current.knownSubtotal, unit)}<small>{coverage(comparison.current)}</small></td>
          <td>{metric(comparison.previous.knownSubtotal, unit)}<small>{coverage(comparison.previous)}</small></td>
          <td>{derived(comparison.percentageChange, 'percent')}</td>
        </tr>
      })}
    </Table>
  </div>
}

function Volume({ buckets, totals }: { buckets: Schemas['InsightBucketResult'][]; totals: Schemas['InsightTotals'] }) {
  return <Section title={t.volume}>
    <p>{t.volumeHelp}</p><p className="muted">{t.chartHelp}</p>
    {totalsMetrics.slice(0, 3).map(([key, label, unit]) => <div key={key}>
      <h3>{label}</h3>
      <p>{t.selectedSubtotal}: <strong>{metric(totals[key].knownSubtotal, unit)}</strong> · {coverage(totals[key])}</p>
      <Plot title={label} unit={unitLabel(unit)} points={buckets.map(({ period }) => ({
        label: period.from, value: scaled(period.totals[key].knownSubtotal, unit),
      }))} />
    </div>)}
    <details><summary>{t.dataTableFor(t.bucketTable)}</summary>
      <Table caption={t.bucketTable} headers={[t.period, t.distance, t.duration, t.gain, t.restSessions]}>
        {buckets.map(bucket => <tr key={bucket.periodStart}>
          <th scope="row">{bucket.period.from} – {bucket.period.to}
            {bucket.isoWeekNumber !== null && <small>{t.week} {bucket.isoWeekYear}–{bucket.isoWeekNumber}</small>}
          </th>
          {totalsMetrics.slice(0, 3).map(([key, , unit]) => <td key={key}>{metric(bucket.period.totals[key].knownSubtotal, unit)}
            <small>{coverage(bucket.period.totals[key])}</small></td>)}
          <td>{number(bucket.period.totals.restSessionCount, 0)}</td>
        </tr>)}
      </Table>
    </details>
  </Section>
}

function bandText(band: Schemas['LoadBand'] | null, why: Schemas['UnavailableReason'] | null) {
  return band ? t.bandLabels[band] : `${t.unavailable} — ${reason(why)}`
}

function Load({ value, series, to }: { value: Schemas['LoadInsights']; series: Schemas['LoadSeriesPoint'][]; to: string }) {
  const empty = series.length === 0
  return <>
    <Section title={t.trainingLoad}>
      <p>{t.loadHelp}</p>
      <p>{t.endingOn(to)} {t.acute}: <strong>{metric(value.acute7.knownSubtotal, 'load')}</strong> ({coverage(value.acute7)})
        · {t.chronic}: <strong>{metric(value.chronicWeeklyKnownSubtotal, 'load')}</strong> ({t.chronicCoverage}: {coverage(value.chronic28)})</p>
      {empty ? <p>{t.noSeries}</p> : <>
        <p className="muted">{t.loadSeriesHelp} {t.chartHelp}</p>
        <SeriesPlot title={t.loadChart} unit={t.loadUnit} series={[
          { key: 'acute', name: t.acuteSeries }, { key: 'chronic', name: t.chronicSeries, dashed: true },
        ]} points={series.map(point => ({
          label: point.date,
          // Incomplete windows are gaps: a known subtotal is never plotted as if it were the full load.
          acute: point.acute7.isComplete === false ? null : finite(point.acute7.knownSubtotal),
          chronic: point.chronic28.isComplete === false ? null : finite(point.chronicWeeklyKnownSubtotal),
        }))} />
        <details><summary>{t.dataTableFor(t.loadChart)}</summary>
          <Table caption={t.loadChart} headers={[t.date, t.acute, t.coverage, t.chronic, t.chronicCoverage]}>
            {series.map(point => <tr key={point.date}><th scope="row">{point.date}</th>
              <td>{metric(point.acute7.knownSubtotal, 'load')}</td><td>{coverage(point.acute7)}</td>
              <td>{metric(point.chronicWeeklyKnownSubtotal, 'load')}</td><td>{coverage(point.chronic28)}</td></tr>)}
          </Table>
        </details>
      </>}
    </Section>
    <Section title={t.acwrSection}>
      <p>{t.ratioHelp}</p><p className="muted">{t.bands}</p>
      {value.bandReason === 'insufficientHistory' && <p className="notice">{t.limited}</p>}
      <p>{t.firstActivity}: {value.firstCompletedActivityDate ?? t.unavailable}</p>
      <p>{t.endingOn(to)} {t.acwr}: <strong>{derived(value.acwr)}</strong> · {t.band}: <strong>{bandText(value.band, value.bandReason)}</strong></p>
      {empty ? <p>{t.noSeries}</p> : <>
        <p className="muted">{t.chartHelp}</p>
        <Plot title={t.acwrChart} unit={t.ratioUnit} kind="line"
          points={series.map(point => ({ label: point.date, value: finite(point.acwr.value) }))} />
        <details><summary>{t.dataTableFor(t.acwrChart)}</summary>
          <Table caption={t.acwrChart} headers={[t.date, t.acwr, t.band]}>
            {series.map(point => <tr key={point.date}><th scope="row">{point.date}</th>
              <td>{derived(point.acwr)}</td><td>{bandText(point.band, point.bandReason)}</td></tr>)}
          </Table>
        </details>
      </>}
    </Section>
    <Section title={t.monotonySection}>
      <p>{t.monotonyHelp}</p>
      <p>{t.endingOn(to)} {t.monotony}: <strong>{derived(value.monotony)}</strong></p>
      {empty ? <p>{t.noSeries}</p> : <>
        <p className="muted">{t.chartHelp}</p>
        <Plot title={t.monotonyChart} unit={t.ratioUnit} kind="line"
          points={series.map(point => ({ label: point.date, value: finite(point.monotony.value) }))} />
        <details><summary>{t.dataTableFor(t.monotonyChart)}</summary>
          <Table caption={t.monotonyChart} headers={[t.date, t.monotony]}>
            {series.map(point => <tr key={point.date}><th scope="row">{point.date}</th><td>{derived(point.monotony)}</td></tr>)}
          </Table>
        </details>
      </>}
    </Section>
  </>
}

function Distribution({ values, sessionCount }: { values: Schemas['TypeDistribution'][]; sessionCount: number | string }) {
  return <Section title={t.distribution}>
    <p>{t.distributionHelp}</p><p className="muted">{t.chartHelp}</p>
    <p>{t.sessions}: <strong>{number(sessionCount, 0)}</strong></p>
    <Plot title={t.distribution} unit={t.count} points={values.map(value => ({
      label: typeName(value.typeCode), value: finite(value.sessionCount),
    }))} />
    <Table caption={t.distribution} headers={[t.type, t.count, t.share]}>
      {values.map(value => <tr key={value.typeCode}><th scope="row">{typeName(value.typeCode)}</th>
        <td>{number(value.sessionCount, 0)}</td><td>{number(finite(value.share) === null ? null : Number(value.share) * 100)} %</td></tr>)}
    </Table>
  </Section>
}

function Pace({ data }: { data: Schemas['InsightsResponse'] }) {
  return <Section title={t.pace}>
    <p>{t.paceHelp}</p>
    <p>{t.selectedPace}: <strong>{pace(data.selected.easyPace.secondsPerKilometer)}</strong> · {coverage(data.selected.easyPace)}</p>
    {!data.buckets.some(bucket => finite(bucket.period.easyPace.secondsPerKilometer.value) !== null) && <p>{t.noPace}</p>}
    <p className="muted">{t.chartHelp}</p>
    <Plot title={t.pace} unit={t.paceUnit} kind="line" points={data.buckets.map(({ period }) => ({
      label: period.from, value: finite(period.easyPace.secondsPerKilometer.value),
    }))} />
    <Table caption={t.pace} headers={[t.period, t.paceValue, t.coverage]}>
      {data.buckets.map(({ period, periodStart }) => <tr key={periodStart}><th scope="row">{period.from} – {period.to}</th>
        <td>{pace(period.easyPace.secondsPerKilometer)}</td><td>{coverage(period.easyPace)}</td></tr>)}
    </Table>
  </Section>
}

export function Dashboard({ data }: { data: Schemas['InsightsResponse'] }) {
  return <>
    {finite(data.selected.totals.sessionCount) === 0 && <p role="status">{t.empty}</p>}
    <div className="insights-grid">
      <Totals title={t.selected} period={data.selected} /><Totals title={t.ytd} period={data.yearToDate} />
    </div>
    <Section title={t.comparisons}>
      <p>{t.comparisonsHelp}</p>
      <Comparison title={t.weekComparison} value={data.weekComparison} />
      <Comparison title={t.monthComparison} value={data.monthComparison} />
    </Section>
    <div className="insights-grid"><Volume buckets={data.buckets} totals={data.selected.totals} /><Pace data={data} /></div>
    <Load value={data.trainingLoad} series={data.loadSeries} to={data.to} />
    <Distribution values={data.distribution} sessionCount={data.selected.totals.sessionCount} />
  </>
}
