import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { Navigate, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { en, locale } from '../i18n'
import { addDays, dateKey, dateLabel, duration, isoWeek, km, monthDays, monthLabel, monthPath, today, utcDate, weekTotals } from './calendar'
import { listRuns } from './queries'
import { RunPanel } from './RunPanel'
import { typeGroup } from './runTypes'

export function DiaryPage() {
  const { year: yearParam, month: monthParam } = useParams()
  const navigate = useNavigate()
  const [currentMonth] = useState(() => monthPath(new Date()))
  const year = Number(yearParam), month = Number(monthParam)
  if (!/^\d{4}$/.test(yearParam ?? '') || !/^\d{2}$/.test(monthParam ?? '') ||
    year < 1000 || year > 9998 || month < 1 || month > 12)
    return <Navigate to={currentMonth} replace />
  return <Month key={`${year}-${month}`} year={year} month={month} navigate={navigate} />
}

function Month({ year, month, navigate }: {
  year: number; month: number; navigate: ReturnType<typeof useNavigate>
}) {
  const days = monthDays(year, month)
  const first = new Date(Date.UTC(year, month - 1, 1))
  const firstKey = dateKey(first)
  const location = useLocation()
  const focusDate = location.state?.focusDate as string | undefined
  const focusControl = location.state?.focusControl as string | undefined
  const openDate = location.state?.openDate as string | undefined
  // Deep link from Insights: open once this month's owner-scoped runs are loaded; closing clears it.
  const [pendingOpen, setPendingOpen] = useState(openDate && days.some(day => dateKey(day) === openDate) ? openDate : null)
  const [focused, setFocused] = useState(focusDate && days.some(day => dateKey(day) === focusDate)
    ? focusDate : today().startsWith(firstKey.slice(0, 7)) ? today() : firstKey)
  const [panel, setPanel] = useState<{ date?: string; add?: boolean } | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const grid = useRef<HTMLTableElement>(null)
  useEffect(() => {
    if (focusDate) grid.current?.querySelector<HTMLButtonElement>(`[data-date="${focusDate}"]`)?.focus()
    else if (focusControl) document.getElementById(`calendar-${focusControl}`)?.focus()
  }, [focusDate, focusControl, location.key])
  const query = useQuery({
    queryKey: ['runs', dateKey(days[0]), dateKey(days.at(-1)!)],
    queryFn: ({ signal }) => listRuns(dateKey(days[0]), dateKey(days.at(-1)!), signal),
    retry: false,
  })
  const runs = [...(query.data ?? [])].sort((a, b) => a.startAtUtc.localeCompare(b.startAtUtc))
  function moveMonth(amount: number) {
    navigate(monthPath(new Date(Date.UTC(year, month - 1 + amount, 1))), {
      state: { focusControl: amount < 0 ? 'previous' : 'next' },
    })
  }
  function keyboard(event: KeyboardEvent<HTMLButtonElement>, date: Date) {
    const offsets: Record<string, number> = {
      ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7,
      Home: -((date.getUTCDay() + 6) % 7), End: 6 - ((date.getUTCDay() + 6) % 7),
    }
    if (event.key === 'PageUp' || event.key === 'PageDown') {
      event.preventDefault()
      const target = new Date(Date.UTC(year, month - 1 + (event.key === 'PageUp' ? -1 : 1) * (event.shiftKey ? 12 : 1), 1))
      if (target.getUTCFullYear() >= 1000 && target.getUTCFullYear() <= 9998)
        navigate(monthPath(target), { state: { focusDate: dateKey(target) } })
      return
    }
    if (!(event.key in offsets)) return
    event.preventDefault()
    const next = addDays(date, offsets[event.key])
    const key = dateKey(next)
    if (key < dateKey(days[0]) || key > dateKey(days.at(-1)!)) {
      if (next.getUTCFullYear() >= 1000 && next.getUTCFullYear() <= 9998)
        navigate(monthPath(next), { state: { focusDate: key } })
    } else {
      setFocused(key)
      grid.current?.querySelector<HTMLButtonElement>(`[data-date="${key}"]`)?.focus()
    }
  }
  const ready = query.isSuccess && !query.isFetching
  const activePanel = panel ?? (ready && pendingOpen ? { date: pendingOpen } : null)
  const shownRuns = activePanel?.date ? runs.filter(run => run.startAtUtc.slice(0, 10) === activePanel.date)
    : runs.filter(run => run.startAtUtc.slice(0, 7) === firstKey.slice(0, 7))
  return <main className="diary">
    <p className="eyebrow">{en.calendar.eyebrow}</p>
    <div className="month-heading">
      <h1><button data-panel-return onClick={() => setPanel({})} disabled={!ready}>{monthLabel(first)}</button></h1>
      <button className="primary" disabled={!ready} onClick={() => setPanel({
        date: focused, add: true,
      })}>{en.add}</button>
    </div>
    <div className="toolbar month-controls" aria-label={en.calendar.navigation}>
      <button id="calendar-previous" aria-label={en.calendar.previous} disabled={year === 1000 && month === 1} onClick={() => moveMonth(-1)}>←</button>
      <button onClick={() => navigate(monthPath(new Date()), { state: { focusDate: today() } })}>{en.calendar.today}</button>
      <button id="calendar-next" aria-label={en.calendar.next} disabled={year === 9998 && month === 12} onClick={() => moveMonth(1)}>→</button>
      <label>{en.calendar.month}<select id="calendar-month" value={month} onChange={event =>
        navigate(monthPath(new Date(Date.UTC(year, Number(event.target.value) - 1, 1))), { state: { focusControl: 'month' } })}>
        {Array.from({ length: 12 }, (_, i) => <option key={i} value={i + 1}>
          {new Date(Date.UTC(2026, i, 1)).toLocaleDateString(locale, { month: 'long', timeZone: 'UTC' })}
        </option>)}
      </select></label>
      <form className="toolbar" onSubmit={event => {
          event.preventDefault()
          const value = Number(new FormData(event.currentTarget).get('year'))
          if (Number.isInteger(value) && value >= 1000 && value <= 9998)
            navigate(monthPath(new Date(Date.UTC(value, month - 1, 1))), { state: { focusControl: 'year' } })
        }}>
        <label>{en.calendar.year}<input id="calendar-year" name="year" type="number"
          defaultValue={year} required min={1000} max={9998} /></label>
        <button type="submit" aria-label={en.calendar.goToYear}>{en.calendar.go}</button>
      </form>
    </div>
    <p className="legend">{en.calendar.legend}</p>
    <p id="calendar-help" className="sr-only">{en.calendar.help}</p>
    <div role="status" aria-live="polite">{query.isFetching ? en.loading : announcement}</div>
    {query.isError && <div role="alert">{query.error.message} <button onClick={() => query.refetch()}>{en.calendar.retry}</button></div>}
    <div className="calendar-scroll">
      <table ref={grid} className="calendar" role="grid" aria-label={monthLabel(first)}
        aria-describedby="calendar-help" aria-busy={query.isFetching}>
        <thead><tr role="row">
          <th role="columnheader" scope="col">{en.calendar.week}<br /><small>{en.calendar.completedTotals}</small></th>
          {en.weekdays.map(day => <th role="columnheader" scope="col" key={day} abbr={day}><span aria-hidden="true">{day.slice(0, 3)}</span><span className="sr-only">{day}</span></th>)}
        </tr></thead>
        <tbody>{Array.from({ length: days.length / 7 }, (_, index) => {
          const week = days.slice(index * 7, index * 7 + 7)
          const start = dateKey(week[0]), end = dateKey(week[6])
          const totals = weekTotals(runs.filter(run => run.startAtUtc.slice(0, 10) >= start && run.startAtUtc.slice(0, 10) <= end))
          const iso = isoWeek(week[0])
          return <tr role="row" key={start}>
            <th role="rowheader" scope="row" aria-label={en.calendar.weekLabel(iso.week, iso.year)}>
              W{iso.week}
              {query.isSuccess && <small>{km(totals.distance)} km<br />{duration(totals.seconds)}<br />↑ {totals.elevation.toLocaleString(locale)} m</small>}
            </th>
            {week.map(date => {
              const key = dateKey(date)
              const dayRuns = runs.filter(run => run.startAtUtc.slice(0, 10) === key)
              return <td role="gridcell" key={key} aria-selected={focused === key}
                className={date.getUTCMonth() !== month - 1 ? 'outside-month' : ''}>
                <button className="day" data-date={key} tabIndex={focused === key ? 0 : -1}
                  aria-current={key === today() ? 'date' : undefined}
                  aria-label={en.calendar.dayLabel(dateLabel(date), dayRuns.length, dayRuns.map(run => `${run.status}, ${run.runTypeName}, ${km(run.distanceMeters)} km`).join('; '))}
                  onFocus={() => setFocused(key)} onKeyDown={event => keyboard(event, date)}
                  onClick={() => { if (ready) setPanel({ date: key }) }}>
                  <span className="day-number">{date.getUTCDate()}</span>
                  {dayRuns.map(run => <span key={run.id}
                    className={`bubble ${run.status.toLowerCase()} type-${typeGroup(run.runTypeCode)}`}>
                    {typeGroup(run.runTypeCode) === 'race' ? '🏆 ' : ''}{run.runTypeName}
                    <strong>{run.distanceMeters == null ? '—' : `${km(run.distanceMeters)} km`}</strong>
                    <small>{run.status}</small>
                  </span>)}
                </button>
              </td>
            })}
          </tr>
        })}</tbody>
      </table>
    </div>
    <p className="muted">{en.calendar.totalsHelp}</p>
    {activePanel && <RunPanel runs={shownRuns} date={activePanel.date ?? firstKey}
      label={activePanel.date ? dateLabel(utcDate(activePanel.date)) : monthLabel(first)}
      initialAdd={'add' in activePanel ? activePanel.add : undefined} onClose={() => { setPanel(null); setPendingOpen(null) }} onChanged={() => setAnnouncement(en.calendar.updated)} />}
  </main>
}
