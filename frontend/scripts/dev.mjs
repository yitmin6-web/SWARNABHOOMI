import { spawn } from 'node:child_process'
import { setTimeout as delay } from 'node:timers/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const frontendDirectory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repositoryDirectory = path.resolve(frontendDirectory, '..')
const apiDirectory = path.join(
  repositoryDirectory,
  'backend',
  'src',
  'SWARNABHOOMI.Api',
)
const apiProject = path.join(
  apiDirectory,
  'SWARNABHOOMI.Api.csproj',
)
const apiAssembly = path.join(apiDirectory, 'bin', 'Debug', 'net10.0', 'SWARNABHOOMI.Api.dll')

let buildProcess
let apiProcess
let frontendProcess
let stopping = false

function stopProcesses() {
  if (stopping) return
  stopping = true
  buildProcess?.kill('SIGTERM')
  frontendProcess?.kill('SIGTERM')
  apiProcess?.kill('SIGTERM')
}

process.once('SIGINT', stopProcesses)
process.once('SIGTERM', stopProcesses)

function buildApi() {
  return new Promise((resolve, reject) => {
    buildProcess = spawn('dotnet', ['build', apiProject], {
      cwd: repositoryDirectory,
      stdio: 'inherit',
    })
    buildProcess.once('error', reject)
    buildProcess.once('close', (code) => {
      buildProcess = undefined
      if (code === 0) resolve()
      else reject(new Error(`The API build exited with code ${code ?? 'unknown'}.`))
    })
  })
}

try {
  console.log('Building the API...')
  await buildApi()

  if (stopping) throw new Error('Startup was interrupted.')

  apiProcess = spawn(
    'dotnet',
    [apiAssembly, '--urls', 'http://127.0.0.1:5025'],
    {
      cwd: apiDirectory,
      stdio: 'inherit',
      env: {
        ...process.env,
        DOTNET_ENVIRONMENT: 'Development',
        ASPNETCORE_ENVIRONMENT: 'Development',
      },
    },
  )

  apiProcess.once('error', (error) => {
    console.error(`Unable to start the API: ${error.message}`)
    stopProcesses()
  })

  let apiReady = false
  for (let attempt = 0; attempt < 120; attempt += 1) {
    if (stopping || apiProcess.exitCode !== null || apiProcess.signalCode !== null) {
      throw new Error('The API exited before it became ready. Check the API output above.')
    }

    try {
      const response = await fetch('http://127.0.0.1:5025/api/health', {
        signal: AbortSignal.timeout(1000),
      })
      if (response.ok) {
        apiReady = true
        break
      }
      await delay(500)
    } catch {
      await delay(500)
    }
  }

  if (!apiReady) {
    throw new Error('The API did not become ready within 60 seconds.')
  }

  console.log('API is ready. Starting the frontend...')
  const viteEntry = path.join(frontendDirectory, 'node_modules', 'vite', 'bin', 'vite.js')
  frontendProcess = spawn(process.execPath, [viteEntry], {
    cwd: frontendDirectory,
    stdio: 'inherit',
  })

  frontendProcess.once('error', (error) => {
    console.error(`Unable to start the frontend: ${error.message}`)
    process.exitCode = 1
    stopProcesses()
  })

  await new Promise((resolve) => {
    frontendProcess.once('close', (code) => {
      const wasStopping = stopping
      stopProcesses()
      resolve(wasStopping ? process.exitCode ?? 0 : code ?? 1)
    })
    apiProcess.once('close', (code) => {
      if (!stopping) {
        console.error(`The API exited${code === null ? '' : ` with code ${code}`}.`)
        stopProcesses()
        resolve(code ?? 1)
      }
    })
  }).then((code) => {
    process.exitCode = code
  })
} catch (error) {
  if (!stopping) console.error(error instanceof Error ? error.message : error)
  stopProcesses()
  process.exitCode = stopping ? 0 : 1
}
