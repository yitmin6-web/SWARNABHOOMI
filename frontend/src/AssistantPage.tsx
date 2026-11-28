import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Message = {
  role: 'user' | 'assistant'
  content: string
}

type WeatherMeta = {
  weatherSource?: string
  forecastLocation?: string
  generatedAtUtc?: string
}

const QUICK_DECISION_PROMPTS = [
  'I plan to weed tomorrow — is it safe with the 7-day weather?',
  'Analyze my harvest predictions, risks & opportunities',
  'Which day this week is safest for fertilizer or spraying?',
  'Compare current market prices and recommend my best next decision',
] as const

export default function AssistantPage() {
  const [messages, setMessages] = useState<Message[]>([])
  const [prompt, setPrompt] = useState('')
  const [error, setError] = useState('')
  const [sending, setSending] = useState(false)
  const [weatherMeta, setWeatherMeta] = useState<WeatherMeta | null>(null)
  const conversationEnd = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    conversationEnd.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, sending])

  const submitPrompt = async (rawContent: string) => {
    const content = rawContent.trim()
    if (!content || sending) return

    const nextMessages = [...messages, { role: 'user' as const, content }]
    setMessages(nextMessages)
    setPrompt('')
    setError('')
    setSending(true)
    try {
      const response = await fetch('/api/assistant/chat', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ messages: nextMessages }),
      })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { detail?: string; error?: string; title?: string } | null
        const detail = result?.detail || result?.error || result?.title
        throw new Error(detail
          ? `Assistant request failed (${response.status}): ${detail}`
          : `Assistant request failed (${response.status}).`)
      }
      if (!response.body) throw new Error('The assistant response could not be streamed.')

      setMessages((current) => [...current, { role: 'assistant', content: '' }])
      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      let reply = ''

      while (true) {
        const { value, done } = await reader.read()
        buffer += decoder.decode(value, { stream: !done })

        let eventEnd = buffer.indexOf('\n\n')
        while (eventEnd >= 0) {
          const event = buffer.slice(0, eventEnd)
          buffer = buffer.slice(eventEnd + 2)
          const data = event.split(/\r?\n/).find((line) => line.startsWith('data:'))
          if (data) {
            const chunk = JSON.parse(data.slice(5).trim()) as {
              content?: string
              error?: string
              weatherSource?: string
              forecastLocation?: string
              generatedAtUtc?: string
            }
            if (chunk.error) throw new Error(chunk.error)
            if (chunk.weatherSource || chunk.forecastLocation) {
              setWeatherMeta({
                weatherSource: chunk.weatherSource,
                forecastLocation: chunk.forecastLocation,
                generatedAtUtc: chunk.generatedAtUtc,
              })
            }
            if (chunk.content) {
              reply += chunk.content
              setMessages((current) => current.map((message, index) =>
                index === current.length - 1 && message.role === 'assistant'
                  ? { ...message, content: reply }
                  : message))
            }
          }
          eventEnd = buffer.indexOf('\n\n')
        }

        if (done) break
      }

      if (!reply.trim()) throw new Error('The assistant returned an empty response.')
    } catch (requestError) {
      setMessages((current) => current.filter((message, index) =>
        !(index === current.length - 1 && message.role === 'assistant' && !message.content)))
      setError(requestError instanceof Error ? requestError.message : 'Unable to reach the AI assistant.')
    } finally {
      setSending(false)
    }
  }

  const send = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    await submitPrompt(prompt)
  }

  return (
    <section className="extension-card assistant-card">
      <div className="extension-toolbar">
        <div>
          <span className="panel-eyebrow">SWARNABHOOMI SUPPORT</span>
          <h2>Ask the AI assistant</h2>
          <p>Database-grounded agronomic predictions, risk &amp; opportunity intelligence, and 7-day task-safety timing.</p>
          {weatherMeta?.weatherSource && (
            <div className="live-status" style={{ marginTop: 8 }}>
              <i />
              <span>
                {weatherMeta.forecastLocation ? `${weatherMeta.forecastLocation} · ` : ''}
                {weatherMeta.weatherSource}
              </span>
            </div>
          )}
        </div>
        <button
          type="button"
          className="extension-secondary-button"
          onClick={() => { setMessages([]); setError(''); setWeatherMeta(null) }}
          disabled={!messages.length}
        >
          New conversation
        </button>
      </div>

      <p className="assistant-disclaimer">
        Replies are AI-generated from live platform records and 7-day weather forecasts. Always verify critical field interventions with your local Agrarian Officer.
        Avoid sharing personal information, passwords, or other secrets. This conversation is not saved after you leave this page.
      </p>

      <div className="assistant-conversation" aria-live="polite">
        {!messages.length && (
          <div className="assistant-welcome">
            <span className="assistant-mark">✦</span>
            <strong>How can I help?</strong>
            <p>Ask about harvest forecasts, field risks, market opportunities, or whether planned tasks like weeding, spraying, or fertilizing are safe this week.</p>
          </div>
        )}
        {messages.map((message, index) => (
          <article className={`assistant-message assistant-message-${message.role}`} key={`${message.role}-${index}`}>
            <strong>{message.role === 'user' ? 'You' : 'SWARNABHOOMI assistant'}</strong>
            <p>{message.content}</p>
          </article>
        ))}
        {sending && <p className="assistant-typing" role="status">Analyzing records &amp; 7-day forecast…</p>}
        <div ref={conversationEnd} />
      </div>

      {error && <p className="extension-alert extension-alert-error" role="alert">{error}</p>}
      <div className="toolbar-button-group" style={{ marginBottom: 10 }}>
        {QUICK_DECISION_PROMPTS.map((quickPrompt) => (
          <button
            key={quickPrompt}
            type="button"
            className="extension-secondary-button"
            style={{ minHeight: 34, padding: '6px 10px', fontSize: '0.8rem', fontWeight: 550 }}
            disabled={sending}
            onClick={() => void submitPrompt(quickPrompt)}
          >
            {quickPrompt}
          </button>
        ))}
      </div>
      <form className="assistant-input-form" onSubmit={(event) => void send(event)}>
        <label className="sr-only" htmlFor="assistant-prompt">Your question</label>
        <textarea
          id="assistant-prompt"
          value={prompt}
          onChange={(event) => setPrompt(event.target.value)}
          placeholder="Type a question…"
          rows={2}
          maxLength={3000}
          required
        />
        <button className="extension-primary-button" disabled={sending || !prompt.trim()}>
          {sending ? 'Sending…' : 'Send'}
        </button>
      </form>
      <small className="assistant-input-hint">Up to 3,000 characters per message. Limited to 20 requests per user every 15 minutes.</small>
    </section>
  )
}
