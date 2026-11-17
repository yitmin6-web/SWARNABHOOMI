import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'

const geminiBaseUrl = 'https://generativelanguage.googleapis.com/v1beta/openai/'
const geminiModel = 'gemini-4-hydrogen'

type AssistantConfiguration = {
  isConfigured: boolean
  baseUrl: string
  model: string
  hasApiKey: boolean
  isEnabled: boolean
}

export default function AssistantConfigPage() {
  const [configuration, setConfiguration] = useState<AssistantConfiguration>({
    isConfigured: false,
    baseUrl: geminiBaseUrl,
    model: geminiModel,
    hasApiKey: false,
    isEnabled: false,
  })
  const [apiKey, setApiKey] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    let active = true
    const load = async () => {
      try {
        const response = await fetch('/api/admin/assistant/config', { credentials: 'include' })
        if (!response.ok) throw new Error(`Configuration request failed (${response.status}).`)
        const result = await response.json() as AssistantConfiguration
        if (active) setConfiguration(result)
      } catch (requestError) {
        if (active) setError(requestError instanceof Error ? requestError.message : 'Unable to load assistant settings.')
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [])

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setSaving(true)
    setError('')
    setNotice('')
    try {
      const response = await fetch('/api/admin/assistant/config', {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...configuration, apiKey }),
      })
      const body = await response.json().catch(() => null) as AssistantConfiguration & { error?: string } | null
      if (!response.ok) throw new Error(body?.error || `Save failed (${response.status}).`)
      if (!body) throw new Error('The server did not return the saved configuration.')
      setConfiguration(body)
      setApiKey('')
      setNotice('AI assistant settings saved. The API key remains hidden.')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unable to save assistant settings.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="extension-card assistant-config-card">
      <div className="extension-toolbar">
        <div>
          <span className="panel-eyebrow">SUPER ADMINISTRATOR</span>
          <h2>AI assistant configuration</h2>
          <p>Configure Gemini or another OpenAI-compatible provider for signed-in members.</p>
        </div>
        <span className={`assistant-config-status ${configuration.isConfigured ? 'assistant-config-ready' : ''}`}>
          {configuration.isConfigured ? 'Configured' : 'Not configured'}
        </span>
      </div>

      <div className="assistant-secret-notice">
        The API key is encrypted on the server and is never returned to the browser or written to the audit log.
        In production, persist and protect the ASP.NET Data Protection key ring across deployments.
      </div>

      {error && <p className="extension-alert extension-alert-error" role="alert">{error}</p>}
      {notice && <p className="extension-alert extension-alert-success" role="status">{notice}</p>}

      {loading ? <p className="extension-empty">Loading assistant settings…</p> : (
        <form className="extension-form assistant-settings-form" onSubmit={(event) => void save(event)}>
          <button
            type="button"
            className="extension-secondary-button"
            onClick={() => setConfiguration((current) => ({
              ...current,
              baseUrl: geminiBaseUrl,
              model: geminiModel,
            }))}
          >
            Use Gemini settings
          </button>
          <label>API base URL
            <input
              type="url"
              value={configuration.baseUrl}
              onChange={(event) => setConfiguration((current) => ({ ...current, baseUrl: event.target.value }))}
              placeholder={geminiBaseUrl}
              required
              maxLength={500}
            />
            <small>Use an HTTPS OpenAI-compatible API endpoint. Local HTTP endpoints are allowed only in Development.</small>
          </label>
          <label>Model
            <input
              value={configuration.model}
              onChange={(event) => setConfiguration((current) => ({ ...current, model: event.target.value }))}
              required
              maxLength={200}
            />
          </label>
          <label>API key
            <input
              type="password"
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              placeholder={configuration.hasApiKey ? 'Key saved; enter a new key only to rotate it' : 'Enter your Gemini API key'}
              autoComplete="new-password"
              maxLength={4000}
            />
          </label>
          <label className="assistant-enable-toggle">
            <input
              type="checkbox"
              checked={configuration.isEnabled}
              onChange={(event) => setConfiguration((current) => ({ ...current, isEnabled: event.target.checked }))}
            />
            Enable assistant for signed-in members
          </label>
          <button className="extension-primary-button" disabled={saving}>
            {saving ? 'Saving…' : 'Save configuration'}
          </button>
        </form>
      )}
    </section>
  )
}
