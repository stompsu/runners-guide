import { runTypes } from '../diary/runTypes'

export function typeName(code: string) {
  return runTypes.find(type => type[0] === code)?.[1] ?? code
}
