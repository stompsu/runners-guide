import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { en } from '../i18n'
import { runTypes, typeGroup } from './runTypes'
import { useRunMutation } from './queries'
import type { Run, RunInput } from './calendar'

const optionalNumber = (label: string, min: number, max: number, integer = false) =>
  z.string().refine(value => value === '' ||
    (Number.isFinite(Number(value)) && Number(value) >= min && Number(value) <= max &&
      (!integer || Number.isInteger(Number(value)))), en.validation.number(label, min, max, integer))
const raceSchema = z.object({
  raceName: z.string().max(200), category: z.string().max(100), bib: z.string().max(50),
  target: optionalNumber(en.form.labels.target, 1 / 60, 2147483647 / 60),
  result: optionalNumber(en.form.labels.result, 1 / 60, 2147483647 / 60),
  overall: optionalNumber(en.form.labels.overall, 1, 2147483647, true),
  categoryPlace: optionalNumber(en.form.labels.categoryPlace, 1, 2147483647, true),
  resultsUrl: z.string().max(2048).refine(value => !value || /^https?:\/\/\S+$/i.test(value), en.validation.url),
})
const schema = z.object({
  date: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, en.validation.chooseDate).refine(value =>
    Number.isFinite(Date.parse(`${value}T00:00:00Z`)) &&
      new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value &&
      value >= '1000-01-01' && value <= '9998-12-31',
  en.validation.validDate),
  time: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d(:[0-5]\d)?$/, en.validation.chooseTime),
  title: z.string().trim().min(1, en.validation.title).max(200),
  status: z.enum(['Planned', 'Completed']),
  runTypeCode: z.string().refine(code => runTypes.some(type => type[0] === code), en.validation.type),
  notes: z.string().max(4000),
  distance: optionalNumber(en.form.labels.distance, 0.001, 500),
  minutes: optionalNumber(en.form.labels.minutes, 1 / 60, 10080),
  gain: optionalNumber(en.form.labels.gain, 0, 100000),
  loss: optionalNumber(en.form.labels.loss, 0, 100000),
  average: optionalNumber(en.form.labels.average, 30, 250, true),
  maximum: optionalNumber(en.form.labels.maximum, 30, 250, true),
  effort: optionalNumber(en.form.labels.effort, 1, 10, true),
  gear: z.string().max(200), weather: z.string().max(100), surface: z.string().max(50),
  stars: z.string().regex(/^[1-5]?$/), comment: z.string().max(1000),
  raceName: z.string(), category: z.string(), bib: z.string(), target: z.string(), result: z.string(),
  overall: z.string(), categoryPlace: z.string(), resultsUrl: z.string(),
}).superRefine((value, context) => {
  if (typeGroup(value.runTypeCode) === 'race') {
    const result = raceSchema.safeParse(value)
    if (!result.success) result.error.issues.forEach(issue =>
      context.addIssue({ code: 'custom', path: issue.path, message: issue.message }))
  }
  if (value.average && value.maximum && Number(value.maximum) < Number(value.average))
    context.addIssue({ code: 'custom', path: ['maximum'], message: en.validation.heartRate })
  if (value.status === 'Completed' && value.comment.trim() && !value.stars)
    context.addIssue({ code: 'custom', path: ['stars'], message: en.validation.rating })
})
type FormValues = z.infer<typeof schema>
const text = (value: unknown) => value == null ? '' : String(value)
const numeric = (value: string, factor = 1) => value === '' ? null : Number(value) * factor

export function RunForm({ date, run, onSaved, onCancel }: {
  date: string; run?: Run; onSaved: () => void; onCancel: () => void
}) {
  const mutation = useRunMutation()
  const { register, handleSubmit, control, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      date: run?.startAtUtc.slice(0, 10) ?? date, time: run?.startAtUtc.slice(11, 19) ?? '08:00',
      title: run?.title ?? '', status: run?.status ?? 'Planned', runTypeCode: run?.runTypeCode ?? 'easy-run',
      notes: run?.notes ?? '', distance: run?.distanceMeters == null ? '' : String(Number(run.distanceMeters) / 1000),
      minutes: run?.durationSeconds == null ? '' : String(Number(run.durationSeconds) / 60),
      gain: text(run?.elevationGainMeters), loss: text(run?.elevationLossMeters),
      average: text(run?.averageHeartRateBpm), maximum: text(run?.maximumHeartRateBpm),
      effort: text(run?.perceivedEffort), gear: run?.shoesOrGear ?? '', weather: run?.weather ?? '',
      surface: run?.surface ?? '', stars: text(run?.rating?.stars), comment: run?.rating?.comment ?? '',
      raceName: run?.raceDetails?.raceName ?? '', category: run?.raceDetails?.officialDistanceCategory ?? '',
      bib: run?.raceDetails?.bibNumber ?? '',
      target: run?.raceDetails?.targetTimeSeconds == null ? '' : String(Number(run.raceDetails.targetTimeSeconds) / 60),
      result: run?.raceDetails?.resultTimeSeconds == null ? '' : String(Number(run.raceDetails.resultTimeSeconds) / 60),
      overall: text(run?.raceDetails?.overallPlace), categoryPlace: text(run?.raceDetails?.categoryPlace),
      resultsUrl: run?.raceDetails?.resultsUrl ?? '',
    },
  })
  const status = useWatch({ control, name: 'status' })
  const isRace = typeGroup(useWatch({ control, name: 'runTypeCode' })) === 'race'
  const distance = Number(useWatch({ control, name: 'distance' }))
  const minutes = Number(useWatch({ control, name: 'minutes' }))
  function field(name: Exclude<keyof FormValues, 'runTypeCode' | 'stars' | 'status'>, type = 'text', options = {}) {
    const label = en.form.labels[name]
    return <div key={name} className="field">
      <label htmlFor={`run-${name}`}>{label}</label>
      <input id={`run-${name}`} {...register(name)} type={type} {...options}
        aria-invalid={Boolean(errors[name])} aria-describedby={errors[name] ? `${name}-error` : undefined} />
      {errors[name] && <span className="field-error" id={`${name}-error`}>{errors[name]?.message}</span>}
    </div>
  }
  async function submit(value: FormValues) {
    const body: RunInput = {
      startAtUtc: `${value.date}T${value.time}${value.time.length === 5 ? ':00' : ''}Z`, title: value.title, status: value.status,
      runTypeCode: value.runTypeCode, notes: value.notes || null,
      distanceMeters: numeric(value.distance, 1000),
      durationSeconds: value.minutes === '' ? null : Math.round(Number(value.minutes) * 60),
      elevationGainMeters: numeric(value.gain), elevationLossMeters: numeric(value.loss),
      averageHeartRateBpm: numeric(value.average), maximumHeartRateBpm: numeric(value.maximum),
      perceivedEffort: numeric(value.effort), shoesOrGear: value.gear || null,
      weather: value.weather || null, surface: value.surface || null,
      location: run?.location ?? null,
      raceDetails: typeGroup(value.runTypeCode) === 'race' ? {
        raceName: value.raceName || null, officialDistanceCategory: value.category || null,
        bibNumber: value.bib || null, targetTimeSeconds: value.target ? Math.round(Number(value.target) * 60) : null,
        resultTimeSeconds: value.result ? Math.round(Number(value.result) * 60) : null,
        overallPlace: numeric(value.overall), categoryPlace: numeric(value.categoryPlace),
        resultsUrl: value.resultsUrl || null,
      } : null,
      rating: value.status === 'Completed' && value.stars
        ? { stars: Number(value.stars), comment: value.comment || null } : null,
    }
    try {
      await mutation.mutateAsync({ id: run?.id, body })
      onSaved()
    } catch { /* The mutation error is rendered below. */ }
  }
  return <form onSubmit={handleSubmit(submit)} noValidate>
    <fieldset disabled={mutation.isPending} className="form-grid">
      <legend className="sr-only">{en.form.legend}</legend>
      {field('title', 'text', { maxLength: 200 })}
      {field('date', 'date', { min: '1000-01-01', max: '9998-12-31' })}
      {field('time', 'time', { step: 1 })}
      <label>{en.form.labels.status}<select {...register('status')}>
        <option value="Planned">{en.planned}</option><option value="Completed">{en.completed}</option>
      </select></label>
      <label>{en.form.labels.runType}<select {...register('runTypeCode')}>
        {runTypes.map(([code, name]) => <option key={code} value={code}>{name}</option>)}
      </select></label>
      {field('distance', 'number', { step: 'any' })}
      {field('minutes', 'number', { step: 'any' })}
      <p className="muted">{en.form.pace}: {distance > 0 && minutes > 0
        ? `${Math.floor(Math.round(minutes / distance * 60) / 60)}:${String(Math.round(minutes / distance * 60) % 60).padStart(2, '0')} min/km`
        : '—'}</p>
      {field('gain', 'number', { step: 'any' })}
      {field('loss', 'number', { step: 'any' })}
      {field('average', 'number')}
      {field('maximum', 'number')}
      {field('effort', 'number')}
      {field('gear')}
      {field('weather')}
      {field('surface')}
      <label className="full-width">{en.form.labels.notes}<textarea {...register('notes')} maxLength={4000} /></label>
      {isRace && <fieldset className="form-grid full-width race-fields">
        <legend>{en.form.race}</legend>
        {field('raceName', 'text', { maxLength: 200 })}
        {field('category', 'text', { maxLength: 100 })}
        {field('bib', 'text', { maxLength: 50 })}
        {field('target', 'number', { step: 'any' })}
        {field('result', 'number', { step: 'any' })}
        {field('overall', 'number')}
        {field('categoryPlace', 'number')}
        {field('resultsUrl', 'url', { maxLength: 2048 })}
      </fieldset>}
      {status === 'Completed' && <fieldset className="full-width rating">
        <legend>{en.form.rating}</legend>
        <label><input {...register('stars')} type="radio" value="" /> {en.form.noRating}</label>
        {[1, 2, 3, 4, 5].map(stars => <label key={stars}>
          <input {...register('stars')} type="radio" value={stars} />
          <span aria-hidden="true">{'★'.repeat(stars)}</span><span className="sr-only">{en.form.stars(stars)}</span>
        </label>)}
        {errors.stars && <p role="alert" className="field-error">{errors.stars.message}</p>}
        <label>{en.form.labels.comment}<textarea {...register('comment')} maxLength={1000} /></label>
      </fieldset>}
      {Object.keys(errors).length > 0 && <p role="alert" className="field-error full-width">{en.form.check}</p>}
      {mutation.error && <p role="alert" className="field-error full-width">{mutation.error.message}</p>}
      <div className="toolbar full-width">
        <button className="primary" type="submit">{mutation.isPending ? en.form.saving : en.save}</button>
        <button type="button" onClick={onCancel}>{en.cancel}</button>
      </div>
    </fieldset>
  </form>
}
