import { useState, useRef, useEffect, useCallback } from 'react'

const DEFAULT_MODELS = {
  openai:    'gpt-4o-mini',
  deepseek:  'deepseek-chat',
  anthropic: 'claude-haiku-4-5-20251001',
}

const SENDER_CLASS = {
  'Usuario':       'user',
  '🔴 Izquierda':  'left',
  '🔵 Derecha':    'right',
}

const IDLE_SECONDS = 10

export default function App() {
  const [chatLog,        setChatLog]        = useState([])
  const [input,          setInput]          = useState('')
  const [loading,        setLoading]        = useState(false)
  const [showModal,      setShowModal]      = useState(true)
  const [starting,       setStarting]       = useState(false)
  const [conversationId, setConversationId] = useState(null)
  const [statusLine,     setStatusLine]     = useState('')
  const [idleCount,      setIdleCount]      = useState(null)
  const [draft,          setDraft]          = useState({
    provider: 'openai', apiKey: '', model: DEFAULT_MODELS.openai
  })

  const bottomRef      = useRef(null)
  const idleRef        = useRef(null)    // setInterval handle
  const timerActiveRef = useRef(false)   // whether idle timer is running
  const convIdRef      = useRef(null)    // mirror of conversationId for timer callback
  const abortRef       = useRef(null)    // AbortController for the current SSE fetch
  const genRef         = useRef(0)       // generation counter — only the current gen resets state

  convIdRef.current = conversationId

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [chatLog, loading])

  // ── Idle timer ────────────────────────────────────────────────────────────
  function startIdleTimer() {
    clearInterval(idleRef.current)
    timerActiveRef.current = true
    setIdleCount(IDLE_SECONDS)
    let remaining = IDLE_SECONDS
    idleRef.current = setInterval(() => {
      remaining -= 1
      setIdleCount(remaining)
      if (remaining <= 0) {
        clearInterval(idleRef.current)
        timerActiveRef.current = false
        triggerAutoRespond()
      }
    }, 1000)
  }

  function stopIdleTimer() {
    clearInterval(idleRef.current)
    timerActiveRef.current = false
    setIdleCount(null)
  }

  // ── SSE reader ────────────────────────────────────────────────────────────
  // {sender, token}   → onToken   (streaming delta)
  // {sender, content} → onMessage (complete message)
  // {done, ...}       → resolves the promise
  async function readSse(res, { onToken, onMessage, onReplace }) {
    const reader  = res.body.getReader()
    const decoder = new TextDecoder()
    let   buf     = ''
    let   meta    = null

    try {
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buf += decoder.decode(value, { stream: true })
        const lines = buf.split('\n')
        buf = lines.pop()
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          const payload = line.slice(6).trim()
          if (!payload) continue
          try {
            const obj = JSON.parse(payload)
            if (obj.done)                       { meta = obj }
            else if (obj.token   !== undefined)  { onToken(obj.sender, obj.token) }
            else if (obj.replace !== undefined)  { onReplace?.(obj.sender, obj.replace) }
            else if (obj.content !== undefined)  { onMessage(obj.sender, obj.content) }
          } catch { /* ignore malformed */ }
        }
      }
    } catch (err) {
      // AbortError is expected when we cancel the stream intentionally — ignore
      if (err.name !== 'AbortError') throw err
    }
    return meta
  }

  // Append a streaming token to the live bubble for `sender`, or create one.
  function appendToken(sender, token) {
    setChatLog(log => {
      const last = log[log.length - 1]
      if (last && last.sender === sender && last.streaming)
        return [...log.slice(0, -1), { ...last, content: last.content + token }]
      return [...log, { sender, content: token, streaming: true }]
    })
  }

  function appendMessage(sender, content) {
    setChatLog(log => [...log, { sender, content, streaming: false }])
  }

  function finalizeStreaming() {
    setChatLog(log => log.map(m => m.streaming ? { ...m, streaming: false } : m))
  }

  function replaceMessage(sender, content) {
    setChatLog(log => {
      for (let i = log.length - 1; i >= 0; i--) {
        if (log[i].sender === sender)
          return [...log.slice(0, i), { sender, content, streaming: false }, ...log.slice(i + 1)]
      }
      return [...log, { sender, content, streaming: false }]
    })
  }

  // Cancel any in-flight SSE, bump the generation counter, and return both.
  // Only the call that holds the current generation is allowed to reset state in finally.
  function cancelAndGetToken() {
    abortRef.current?.abort()
    abortRef.current = new AbortController()
    const gen = ++genRef.current
    return { signal: abortRef.current.signal, gen }
  }

  // ── Auto-respond (idle timer fires) ──────────────────────────────────────
  const triggerAutoRespond = useCallback(async () => {
    const cid = convIdRef.current
    if (!cid) return
    finalizeStreaming()
    setLoading(true)
    setStatusLine('')
    const { signal, gen } = cancelAndGetToken()
    try {
      const res = await fetch('/api/auto', {
        method: 'POST', signal,
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ conversationId: cid }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const meta = await readSse(res, {
        onToken:   appendToken,
        onMessage: appendMessage,
        onReplace: replaceMessage,
      })
      finalizeStreaming()
      if (meta) setStatusLine(`${meta.historyTotal} msgs · ${meta.latencyMs}ms`)
    } catch (err) {
      if (err.name !== 'AbortError')
        setChatLog(log => [...log, { sender: '⚠ Error', content: err.message }])
    } finally {
      // Only reset state if we're still the active request.
      // A newer call (user message or another auto-turn) may have already taken over.
      if (gen === genRef.current) {
        setLoading(false)
        startIdleTimer()
      }
    }
  }, [])

  function handleProviderChange(e) {
    const p = e.target.value
    setDraft(d => ({ ...d, provider: p, model: DEFAULT_MODELS[p] }))
  }

  // ── Create a debate session ───────────────────────────────────────────────
  async function startSession() {
    if (!draft.apiKey.trim()) return
    abortRef.current?.abort()
    genRef.current++   // invalidate any in-flight request
    setStarting(true)
    try {
      const res = await fetch('/api/start', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ provider: draft.provider, apiKey: draft.apiKey, model: draft.model }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const { conversationId } = await res.json()
      setConversationId(conversationId)
      setChatLog([])
      setStatusLine('')
      stopIdleTimer()
      setShowModal(false)
    } catch (err) {
      alert(`Error al iniciar sesión: ${err.message}`)
    } finally {
      setStarting(false)
    }
  }

  // ── Send user message ─────────────────────────────────────────────────────
  async function sendMessage() {
    const text = input.trim()
    if (!text || loading || !conversationId) return

    stopIdleTimer()
    finalizeStreaming()
    const { signal, gen } = cancelAndGetToken()
    setInput('')
    setChatLog(log => [...log, { sender: 'Usuario', content: text }])
    setLoading(true)
    setStatusLine('')

    try {
      const res = await fetch('/api/message', {
        method: 'POST', signal,
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ conversationId, message: text }),
      })
      if (!res.ok) {
        const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }))
        throw new Error(err.error ?? `HTTP ${res.status}`)
      }
      const meta = await readSse(res, {
        onToken:   appendToken,
        onMessage: appendMessage,
        onReplace: replaceMessage,
      })
      finalizeStreaming()
      if (meta)
        setStatusLine(
          `${meta.historyTotal} msgs · ${meta.rounds} round${meta.rounds !== 1 ? 's' : ''} · ${meta.latencyMs}ms`
        )
    } catch (err) {
      if (err.name !== 'AbortError')
        setChatLog(log => [...log, { sender: '⚠ Error', content: err.message }])
    } finally {
      if (gen === genRef.current) {
        setLoading(false)
        startIdleTimer()
      }
    }
  }

  function handleKey(e) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage() }
  }

  // Reset idle timer on every keystroke (uses ref to avoid stale closure)
  function handleInput(e) {
    setInput(e.target.value)
    if (timerActiveRef.current) startIdleTimer()
  }

  useEffect(() => () => clearInterval(idleRef.current), [])

  const ready = !!conversationId && !loading

  return (
    <div className="app">

      {/* ── Header ──────────────────────────────────────────────────────── */}
      <header>
        <span className="logo">⚔ Debate Político</span>
        <div className="header-right">
          {statusLine && <span className="status">{statusLine}</span>}
          {idleCount !== null && !loading && (
            <span className="idle-timer" title="Los agentes continúan si no escribes">
              ⏳ {idleCount}s
            </span>
          )}
          <button className="btn-icon" onClick={() => { stopIdleTimer(); setShowModal(true) }} title="Nueva sesión">⚙</button>
        </div>
      </header>

      {/* ── Chat ────────────────────────────────────────────────────────── */}
      <main className="chat">
        {chatLog.length === 0 && !loading && (
          <p className="placeholder">
            {conversationId
              ? 'Escribe un tema para iniciar el debate.'
              : 'Configura tu API key para comenzar.'}
          </p>
        )}

        {chatLog.map((m, i) => {
          const cls = SENDER_CLASS[m.sender] ?? 'agent'
          return (
            <div key={i} className={`msg msg-${cls}`}>
              <span className="msg-sender">{m.sender}</span>
              <span className={`msg-content${m.streaming ? ' streaming' : ''}`}>{m.content}</span>
            </div>
          )
        })}

        {loading && (
          <div className="msg msg-loading">
            <span className="msg-sender">…</span>
            <span className="msg-content typing">pensando</span>
          </div>
        )}

        <div ref={bottomRef} />
      </main>

      {/* ── Input ───────────────────────────────────────────────────────── */}
      <footer>
        <input
          value={input}
          onChange={handleInput}
          onKeyDown={handleKey}
          placeholder={ready ? 'Escribe algo o espera que los agentes continúen…' : 'Inicia una sesión primero…'}
          disabled={!ready}
        />
        <button onClick={sendMessage} disabled={!ready || !input.trim()}>
          {loading ? '…' : 'Enviar'}
        </button>
      </footer>

      {/* ── Modal ───────────────────────────────────────────────────────── */}
      {showModal && (
        <div className="overlay">
          <div className="modal">
            <h2>Nueva sesión de debate</h2>
            <p className="modal-hint">
              El servidor crea un <code>DefaultGroupOrchestrator</code> con{' '}
              <code>DebateTurnStrategy</code>: abre el que no habló último, luego el otro,
              luego réplica opcional. Si no escribes en {IDLE_SECONDS}s, los agentes continúan solos.
            </p>

            <label>Proveedor</label>
            <select value={draft.provider} onChange={handleProviderChange}>
              <option value="openai">OpenAI</option>
              <option value="deepseek">DeepSeek</option>
              <option value="anthropic">Anthropic (Claude)</option>
            </select>

            <label>API Key</label>
            <input
              type="password"
              placeholder="sk-…"
              value={draft.apiKey}
              onChange={e => setDraft(d => ({ ...d, apiKey: e.target.value }))}
            />

            <label>Modelo</label>
            <input
              placeholder={DEFAULT_MODELS[draft.provider]}
              value={draft.model}
              onChange={e => setDraft(d => ({ ...d, model: e.target.value }))}
            />

            <div className="modal-footer">
              {conversationId && (
                <button className="btn-secondary" onClick={() => setShowModal(false)}>Cancelar</button>
              )}
              <button onClick={startSession} disabled={!draft.apiKey.trim() || starting}>
                {starting ? 'Iniciando…' : 'Iniciar debate'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
