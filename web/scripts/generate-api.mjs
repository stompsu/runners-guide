import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import openapiTS, { astToString } from 'openapi-typescript'
import ts from 'typescript'

const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(scriptDirectory, '../..')
const apiAssembly = process.env.API_ASSEMBLY_PATH
  ? resolve(process.env.API_ASSEMBLY_PATH)
  : resolve(repositoryRoot, 'src/RunnersDiary.Api/bin/Debug/net10.0/RunnersDiary.Api.dll')
const outputFile = resolve(
  scriptDirectory,
  '../src/api/generated/openapi.d.ts',
)
const baseUrl = process.env.API_BASE_URL ?? 'http://127.0.0.1:5080'
const openApiUrl = `${baseUrl}/openapi/v1.json`

if (!existsSync(apiAssembly)) {
  throw new Error('Build RunnersDiary.Api before generating the API client.')
}

const apiProcess = spawn(
  'dotnet',
  ['exec', apiAssembly, '--urls', baseUrl],
  {
    cwd: repositoryRoot,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' },
    stdio: 'inherit',
  },
)

let apiExit
apiProcess.once('exit', (code, signal) => {
  apiExit = { code, signal }
})

try {
  const deadline = Date.now() + 30_000
  let document
  let lastError

  while (Date.now() < deadline) {
    if (apiExit) {
      throw new Error(
        `API exited before serving OpenAPI (code ${apiExit.code}, signal ${apiExit.signal}).`,
      )
    }

    try {
      const response = await fetch(openApiUrl, {
        signal: AbortSignal.timeout(1_000),
      })

      if (response.ok) {
        document = await response.json()
        break
      }

      lastError = new Error(`OpenAPI endpoint returned HTTP ${response.status}.`)
    } catch (error) {
      lastError = error
    }

    await new Promise((resolvePromise) => setTimeout(resolvePromise, 250))
  }

  if (!document) {
    throw new Error(`Could not load ${openApiUrl} within 30 seconds.`, {
      cause: lastError,
    })
  }

  const ast = await openapiTS(document, {
    // Raw binary request bodies are browser Blobs, not JSON strings.
    transform(schema) {
      if (schema.type === 'string' && schema.format === 'binary')
        return ts.factory.createTypeReferenceNode('Blob')
    },
  })
  await mkdir(dirname(outputFile), { recursive: true })
  await writeFile(outputFile, astToString(ast), 'utf8')
  console.log(`Generated ${outputFile}`)
} finally {
  if (!apiExit) {
    apiProcess.kill()
  }
}
