import { useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { en } from '../i18n'
import { defaultPeriod, periodSearch, readPeriod, type Period } from './period'
import { PeriodControls } from './PeriodControls'
import { insightsOptions } from './queries'
import { Dashboard } from './Dashboard'
import { RaceHistory } from './RaceHistory'
import './insights.css'

function Results({ period }: { period: Period }) {
  const query = useQuery(insightsOptions(period))
  const t = en.insights
  return <>
    {query.isPending && <p role="status">{t.loading}</p>}
    {query.isError && <p role="alert">{query.error.message} <button onClick={() => void query.refetch()}>{t.retry}</button></p>}
    {query.data && <Dashboard data={query.data} />}
    <RaceHistory key={period.to} to={period.to} />
  </>
}

export default function InsightsPage() {
  const [params, setParams] = useSearchParams()
  const period = readPeriod(params)
  const t = en.insights
  const canonical = period ? periodSearch(period) : null
  useEffect(() => {
    if (canonical !== null && params.toString() !== canonical) setParams(canonical, { replace: true })
  }, [canonical, params, setParams])
  return <main className="insights">
    <p className="eyebrow">{t.eyebrow}</p><h1>{t.heading}</h1><p>{t.intro}</p>
    <PeriodControls key={params.toString()} period={period ?? defaultPeriod()} onChange={value => setParams(periodSearch(value))} />
    {period ? <Results period={period} /> : <p role="alert">{t.invalidPeriod}</p>}
  </main>
}
