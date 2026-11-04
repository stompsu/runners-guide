import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { en } from '../i18n'
import { Section, Table } from './Dashboard'
import { typeName } from './typeName'
import { racesOptions } from './queries'
import { metric, number, raceTime } from './format'

function DiaryLink({ date, name }: { date: string; name: string }) {
  // UTC date from the API; the diary month route plus router state opens that day's RunPanel.
  return <Link to={`/diary/${date.slice(0, 4)}/${date.slice(5, 7)}`} state={{ focusDate: date, openDate: date }}
    aria-label={en.insights.openInDiary(name, date)}>{en.insights.viewInDiary}</Link>
}

export function RaceHistory({ to }: { to: string }) {
  const [cursors, setCursors] = useState<(string | undefined)[]>([undefined])
  const [page, setPage] = useState(0)
  const query = useQuery(racesOptions(to, cursors[page]))
  const t = en.insights
  return <Section title={t.races}>
    <p>{t.raceHelp}</p>
    {query.isPending && <p role="status">{t.racesLoading}</p>}
    {query.isError && <p role="alert">{query.error.message} <button onClick={() => void query.refetch()}>{t.retry}</button></p>}
    {query.data && <>
      {query.data.items.length === 0 ? <p role="status">{t.noRaces}</p> :
        <Table caption={t.races} headers={[t.date, t.race, t.category, t.distance, t.duration, t.result, t.overall, t.categoryPlace]}>
          {query.data.items.map(race => <tr key={race.id}>
            <th scope="row">{race.startAtUtc.replace('T', ' ').slice(0, 16)}
              <small><DiaryLink date={race.startAtUtc.slice(0, 10)} name={race.raceName ?? typeName(race.runTypeCode)} /></small></th>
            <td>{race.raceName ?? t.unavailable}<small>{typeName(race.runTypeCode)}</small></td>
            <td>{race.officialDistanceCategory ?? t.unavailable}</td><td>{metric(race.distanceMeters, 'km')}</td>
            <td>{raceTime(race.durationSeconds)}</td><td>{raceTime(race.resultTimeSeconds)}</td>
            <td>{number(race.overallPlace, 0)}</td><td>{number(race.categoryPlace, 0)}</td>
          </tr>)}
        </Table>}
    </>}
    <nav className="toolbar" aria-label={t.racePagination}>
      <button disabled={page === 0 || query.isFetching} onClick={() => setPage(page - 1)}>{t.back}</button>
      <span role="status">{t.page(page + 1)}</span>
      <button disabled={!query.data?.nextCursor || query.isFetching || query.isError} onClick={() => {
        const next = query.data?.nextCursor
        if (!next) return
        setCursors([...cursors.slice(0, page + 1), next])
        setPage(page + 1)
      }}>{t.next}</button>
    </nav>
  </Section>
}
