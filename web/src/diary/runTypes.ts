// Stable seeded codes from RunTypeCatalog; M2 has no run-type lookup endpoint.
export const runTypes = [
  ['road-race', 'Road race', 'race'], ['trail-race', 'Trail race', 'race'],
  ['ultra-road', 'Ultra (road)', 'race'], ['ultra-trail', 'Ultra (trail)', 'race'],
  ['track-race', 'Track race', 'race'], ['cross-country', 'Cross-country', 'race'],
  ['virtual-race', 'Virtual race', 'race'], ['easy-run', 'Easy run', 'easy'],
  ['recovery-run', 'Recovery run', 'easy'], ['long-run', 'Long run', 'long'],
  ['tempo-threshold', 'Tempo / Threshold', 'speed'], ['intervals', 'Intervals', 'speed'],
  ['fartlek', 'Fartlek', 'speed'], ['hill-repeats', 'Hill repeats', 'speed'],
  ['progression-run', 'Progression run', 'speed'], ['race-pace-run', 'Race-pace run', 'speed'],
  ['strides-drills', 'Strides / drills', 'speed'], ['trail-training', 'Trail training', 'long'],
  ['treadmill-run', 'Treadmill run', 'easy'], ['cross-training', 'Cross-training', 'other'],
  ['rest-injury', 'Rest / injury day', 'other'],
] as const
export const typeGroup = (code: string) => runTypes.find(type => type[0] === code)?.[2] ?? 'other'
