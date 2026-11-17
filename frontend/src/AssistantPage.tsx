import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Message = {
  role: 'user' | 'assistant'
  content: string
}

export default function AssistantPage() {
  const [messages, setMessages] = useState<Message[]>([])
  const [prompt, setPrompt] = useState('')
  const [error, setError] = useState('')
  const [sending, setSending] = useState(false)
  const conversationEnd = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    conversationEnd.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, sending])

  const send = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const content = prompt.trim()
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
        const result = await response.json().catch(() => null) as { detail?: string; title?: string } | null
        throw new Error(result?.detail || result?.title || `Assistant request failed (${response.status}).`)
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
            const chunk = JSON.parse(data.slice(5).trim()) as { content?: string; error?: string }
            if (chunk.error) throw new Error(chunk.error)
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

  return (
    <section className="extension-card assistant-card">
      <div className="extension-toolbar">
        <div>
          <span className="panel-eyebrow">SWARNABHOOMI SUPPORT</span>
          <h2>Ask the AI assistant</h2>
          <p>Ask about using the platform and general rice-network workflows.</p>
        </div>
        <button type="button" className="extension-secondary-button" onClick={() => { setMessages([]); setError('') }} disabled={!messages.length}>
          New conversation
        </button>
      </div>

      <p className="assistant-disclaimer">
        Replies are AI-generated and may be incorrect. The assistant does not provide authorized local agronomic rates or guaranteed prices.
        Avoid sharing personal information, passwords, or other secrets. This conversation is not saved after you leave this page.
      </p>

      <div className="assistant-conversation" aria-live="polite">
        {!messages.length && (
          <div className="assistant-welcome">
            <span className="assistant-mark">✦</span>
            <strong>How can I help?</strong>
            <p>Ask about field registration, seasons, cultivation records, demand or how to use SWARNABHOOMI.</p>
          </div>
        )}
        {messages.map((message, index) => (
          <article className={`assistant-message assistant-message-${message.role}`} key={`${message.role}-${index}`}>
            <strong>{message.role === 'user' ? 'You' : 'SWARNABHOOMI assistant'}</strong>
            <p>{message.content}</p>
          </article>
        ))}
        {sending && <p className="assistant-typing" role="status">Thinking…</p>}
        <div ref={conversationEnd} />
      </div>

      {error && <p className="extension-alert extension-alert-error" role="alert">{error}</p>}
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
