import { useState } from 'react'
import { Modal } from '../components/Modal'
import { en } from '../i18n'
import { dateLabel, duration, km, utcDate } from './calendar'
import type { Run } from './calendar'
import { RunForm } from './RunForm'
import { useRunMutation } from './queries'

function Details({ run }: { run: Run }) {
  const fields = [
    [en.details.start, run.startAtUtc.replace('T', ' ').replace('Z', '')],
    [en.form.labels.status, run.status], [en.form.labels.runType, run.runTypeName], [en.form.labels.notes, run.notes],
    [en.details.distance, run.distanceMeters == null ? null : `${km(run.distanceMeters)} km`],
    [en.details.duration, run.durationSeconds == null ? null : duration(run.durationSeconds)],
    [en.details.gain, run.elevationGainMeters == null ? null : `${run.elevationGainMeters} m`],
    [en.details.loss, run.elevationLossMeters == null ? null : `${run.elevationLossMeters} m`],
    [en.details.average, run.averageHeartRateBpm], [en.details.maximum, run.maximumHeartRateBpm],
    [en.details.effort, run.perceivedEffort], [en.form.labels.gear, run.shoesOrGear],
    [en.form.labels.weather, run.weather], [en.form.labels.surface, run.surface], [en.details.location, run.location?.displayName],
    [en.form.labels.raceName, run.raceDetails?.raceName], [en.details.category, run.raceDetails?.officialDistanceCategory],
    [en.form.labels.bib, run.raceDetails?.bibNumber],
    [en.details.target, run.raceDetails?.targetTimeSeconds == null ? null : duration(run.raceDetails.targetTimeSeconds)],
    [en.details.result, run.raceDetails?.resultTimeSeconds == null ? null : duration(run.raceDetails.resultTimeSeconds)],
    [en.form.labels.overall, run.raceDetails?.overallPlace], [en.form.labels.categoryPlace, run.raceDetails?.categoryPlace],
    [en.form.labels.resultsUrl, run.raceDetails?.resultsUrl],
    [en.details.rating, run.status === 'Completed' && run.rating ? en.details.ratingValue(run.rating.stars) : null],
    [en.form.labels.comment, run.status === 'Completed' ? run.rating?.comment : null],
  ]
  return <dl className="details">{fields.filter(([, value]) => value != null && value !== '').map(([label, value]) =>
    <div key={String(label)}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
}

export function RunPanel({ runs, date, label, initialAdd = false, onClose, onChanged }: {
  runs: Run[]; date: string; label: string; initialAdd?: boolean; onClose: () => void; onChanged: () => void
}) {
  const [mode, setMode] = useState<'list' | 'details' | 'form' | 'delete'>(initialAdd ? 'form' : 'list')
  const [id, setId] = useState<string>()
  const mutation = useRunMutation()
  const run = runs.find(item => item.id === id)
  const title = mode === 'form' ? (run ? en.edit : en.add)
    : mode === 'delete' ? en.panel.deleteHeading : mode === 'details' ? run?.title ?? en.panel.unavailable : label
  function select(run: Run, next: typeof mode) {
    setId(run.id)
    mutation.reset()
    setMode(next)
  }
  return <Modal title={title} description={mode === 'delete'
    ? en.panel.deleteWarning
    : en.panel.utc} onClose={onClose}>
    {mode === 'form' && id && !run ? <p role="alert">{en.panel.stale}</p>
      : mode === 'form' ? <RunForm key={id ?? date} date={date} run={run}
      onSaved={() => { onChanged(); onClose() }} onCancel={() => setMode('list')} />
      : mode === 'delete' ? <>
        <p>{run?.title}</p>
        {mutation.error && <p role="alert">{mutation.error.message}</p>}
        <div className="toolbar">
          <button data-cancel-delete disabled={mutation.isPending} onClick={() => setMode('list')}>{en.cancel}</button>
          <button className="danger" disabled={mutation.isPending || !run} onClick={async () => {
            try {
              await mutation.mutateAsync({ id })
              onChanged()
              setMode('list')
            } catch { /* Mutation error stays visible. */ }
          }}>{mutation.isPending ? en.panel.deleting : en.panel.confirm}</button>
        </div>
      </> : mode === 'details' ? <>
        {run ? <><Details run={run} /><button onClick={() => setMode('form')}>{en.edit}</button></>
          : <p role="status">{en.panel.missing}</p>}
        <button onClick={() => setMode('list')}>{en.panel.back}</button>
      </> : <>
        <button className="primary" onClick={() => { setId(undefined); setMode('form') }}>{en.add}</button>
        {runs.length === 0 && <p>{en.empty}</p>}
        <ul className="run-list">{runs.map(run => <li key={run.id}>
          <h3>{run.title}</h3>
          <p>{dateLabel(utcDate(run.startAtUtc.slice(0, 10)))} · {run.startAtUtc.slice(11, 16)} UTC</p>
          <p>{run.status} · {run.runTypeName} · {run.distanceMeters == null ? '—' : `${km(run.distanceMeters)} km`}</p>
          <div className="toolbar" aria-label={en.panel.actions(run.title)}>
            <button onClick={() => select(run, 'details')}>{en.panel.details}</button>
            <button onClick={() => select(run, 'form')}>{en.panel.edit}</button>
            <button onClick={() => select(run, 'delete')}>{en.panel.delete}</button>
          </div>
        </li>)}</ul>
      </>}
  </Modal>
}
