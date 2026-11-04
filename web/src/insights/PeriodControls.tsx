import { useState } from 'react'
import { en } from '../i18n'
import { defaultPeriod, utcToday, validPeriod, type Period } from './period'

export function PeriodControls({ period, onChange }: { period: Period; onChange: (period: Period) => void }) {
  const [draft, setDraft] = useState(period)
  const [invalid, setInvalid] = useState(false)
  const t = en.insights
  return <form className="toolbar insights-controls" aria-label={t.periodControls} onSubmit={event => {
    event.preventDefault()
    if (!validPeriod(draft)) { setInvalid(true); return }
    setInvalid(false)
    onChange(draft)
  }}>
    <label>{t.from}<input type="date" min="0002-01-01" max={utcToday()} required value={draft.from}
      onChange={event => setDraft({ ...draft, from: event.target.value })} /></label>
    <label>{t.to}<input type="date" min="0002-01-01" max={utcToday()} required value={draft.to}
      onChange={event => setDraft({ ...draft, to: event.target.value })} /></label>
    <label>{t.bucket}<select value={draft.bucket} onChange={event =>
      setDraft({ ...draft, bucket: event.target.value as Period['bucket'] })}>
      <option value="week">{t.week}</option><option value="month">{t.month}</option>
    </select></label>
    <button className="primary" type="submit">{t.apply}</button>
    <button type="button" onClick={() => onChange(defaultPeriod())}>{t.thisMonth}</button>
    {invalid && <p role="alert">{t.invalidPeriod}</p>}
  </form>
}
