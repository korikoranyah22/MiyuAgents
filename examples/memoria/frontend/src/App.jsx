import { useState, useRef, useEffect } from 'react'

const DEFAULT_MODELS = {
  openai:    'gpt-4o-mini',
  deepseek:  'deepseek-chat',
  anthropic: 'claude-haiku-4-5-20251001',
}

export default function App() {
  const [chatLog,        setChatLog]        = useState([])
  const [input,          setInput]          = useState('')
  const [loading,        setLoading]        = useState(false)
  const [showStart,      setShowStart]      = useState(true)
  const [starting,       setStarting]       = useState(false)
  const [showMemory,     setShowMemory]     = useState(false)
  const [savingMemory,   setSavingMemory]   = useState(false)
  const [memories,       setMemories]       = useState([])
  const [conversationId, setConversationId] = useState(null)
  const [statusLine,     setStatusLine]     = useState('')
  const [memDraft,       setMemDraft]       = useState({ label: '', value: '' })
  const [draft,          setDraft]          = useState({
    provider:       'openai',
    apiKey:         '',
    model:          DEFAULT_MODELS.openai,
    qdrantHost:     'localhost',
    qdrantPort:     '6333',
    ollamaHost:     'localhost',
    ollamaPort:     '11434',
    embeddingModel: 'embeddinggemma',
  })

  const bottomRef  = useRef(null)
  const abortRef   = useRef(null)
  const genRef     = useRef(0)
  const convIdRef  = useRef(null)

  convIdRef.current = conversationId

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [chatLog, loading])

  useEffect(() => {
    if (!conversationId) return
    const id = setInterval(async () => {
      try {
        const res = await fetch(`/api/memories?conversationId=${conversationId}`)
        if (res.ok) setMemories(await res.json())
      } catch { /* ignore network errors */ }
    }, 3000)
    return () => clearInterval(id)
  }, [conversationId])

  // ── SSE reader ────────────────────────────────────────────────────────────
  async function readSse(res, { onToken }) {
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
            if (obj.done)                      { meta = obj }
            else if (obj.token !== undefined)  { onToken(obj.token) }
          } catch { /* ignore malformed */ }
        }
      }
    } catch (err) {
      if (err.name !== 'AbortError') throw err
    }
    return meta
  }

  function appendToken(token) {
    setChatLog(log => {
      const last = log[log.length - 1]
      if (last && last.role === 'assistant' && last.streaming)
        return [...log.slice(0, -1), { ...last, content: last.content + token }]
      return [...log, { role: 'assistant', content: token, streaming: true }]
    })
  }

  function finalizeStreaming() {
    setChatLog(log => log.map(m => m.streaming ? { ...m, streaming: false } : m))
  }

  function cancelAndGetToken() {
    abortRef.current?.abort()
    abortRef.current = new AbortController()
    const gen = ++genRef.current
    return { signal: abortRef.current.signal, gen }
  }

  function handleProviderChange(e) {
    const p = e.target.value
    setDraft(d => ({ ...d, provider: p, model: DEFAULT_MODELS[p] }))
  }

  // ── Start session ─────────────────────────────────────────────────────────
  async function startSession() {
    if (!draft.apiKey.trim()) return
    setStarting(true)
    try {
      const res = await fetch('/api/start', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({
          provider:       draft.provider,
          apiKey:         draft.apiKey,
          model:          draft.model,
          qdrantHost:     draft.qdrantHost,
          qdrantPort:     parseInt(draft.qdrantPort, 10),
          ollamaHost:     draft.ollamaHost,
          ollamaPort:     parseInt(draft.ollamaPort, 10),
          embeddingModel: draft.embeddingModel,
        }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const { conversationId } = await res.json()
      setConversationId(conversationId)
      setChatLog([])
      setMemories([])
      setStatusLine('')
      setShowStart(false)
    } catch (err) {
      alert(`Error al iniciar sesión: ${err.message}`)
    } finally {
      setStarting(false)
    }
  }

  // ── Send message ──────────────────────────────────────────────────────────
  async function sendMessage() {
    const text = input.trim()
    if (!text || loading || !conversationId) return

    finalizeStreaming()
    const { signal, gen } = cancelAndGetToken()
    setInput('')
    setChatLog(log => [...log, { role: 'user', content: text }])
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
      const meta = await readSse(res, { onToken: appendToken })
      finalizeStreaming()
      if (meta)
        setStatusLine(
          `${meta.historyTotal} msgs · ${meta.memoriesUsed} recuerdo${meta.memoriesUsed !== 1 ? 's' : ''} usado${meta.memoriesUsed !== 1 ? 's' : ''} · ${meta.latencyMs}ms`
        )
    } catch (err) {
      if (err.name !== 'AbortError')
        setChatLog(log => [...log, { role: 'error', content: err.message }])
    } finally {
      if (gen === genRef.current) setLoading(false)
    }
  }

  // ── Save memory ───────────────────────────────────────────────────────────
  async function saveMemory() {
    if (!memDraft.label.trim() || !memDraft.value.trim()) return
    setSavingMemory(true)
    try {
      const res = await fetch('/api/memory', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({
          conversationId,
          label: memDraft.label.trim(),
          value: memDraft.value.trim(),
        }),
      })
      if (!res.ok) {
        const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }))
        throw new Error(err.error ?? err.detail ?? `HTTP ${res.status}`)
      }
      const stored = await res.json()
      setMemories(m => [...m, stored])
      setMemDraft({ label: '', value: '' })
      setShowMemory(false)
    } catch (err) {
      alert(`Error al guardar recuerdo: ${err.message}`)
    } finally {
      setSavingMemory(false)
    }
  }

  function handleKey(e) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage() }
  }

  function handleMemKey(e) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); saveMemory() }
  }

  const ready = !!conversationId && !loading

  return (
    <div className="app">

      {/* ── Header ────────────────────────────────────────────────────────── */}
      <header>
        <span className="logo">🧠 Memoria</span>
        <div className="header-right">
          {statusLine && <span className="status">{statusLine}</span>}
          {conversationId && (
            <button
              className="btn-memory"
              onClick={() => setShowMemory(true)}
              title="Agregar recuerdo"
            >
              + Recuerdo
            </button>
          )}
          <button
            className="btn-icon"
            onClick={() => setShowStart(true)}
            title="Nueva sesión"
          >
            ⚙
          </button>
        </div>
      </header>

      {/* ── Layout ────────────────────────────────────────────────────────── */}
      <div className="layout">

        {/* ── Memory panel ────────────────────────────────────────────────── */}
        {memories.length > 0 && (
          <aside className="memory-panel">
            <p className="panel-title">Recuerdos almacenados</p>
            <ul>
              {memories.map(m => (
                <li key={m.id}>
                  <span className="mem-label">
                    {m.label}
                    {m.source === 'auto' && <span className="mem-badge">auto</span>}
                  </span>
                  <span className="mem-value">{m.value}</span>
                </li>
              ))}
            </ul>
          </aside>
        )}

        {/* ── Chat ────────────────────────────────────────────────────────── */}
        <main className="chat">
          {chatLog.length === 0 && !loading && (
            <p className="placeholder">
              {conversationId
                ? 'Escribe algo. Usa "+ Recuerdo" para inyectar datos en la memoria del agente.'
                : 'Configura la sesión para comenzar.'}
            </p>
          )}

          {chatLog.map((m, i) => {
            if (m.role === 'user')
              return (
                <div key={i} className="msg msg-user">
                  <span className="msg-sender">Tú</span>
                  <span className="msg-content">{m.content}</span>
                </div>
              )
            if (m.role === 'error')
              return (
                <div key={i} className="msg msg-error">
                  <span className="msg-sender">⚠ Error</span>
                  <span className="msg-content">{m.content}</span>
                </div>
              )
            return (
              <div key={i} className="msg msg-agent">
                <span className="msg-sender">Asistente</span>
                <span className={`msg-content${m.streaming ? ' streaming' : ''}`}>{m.content}</span>
              </div>
            )
          })}

          {loading && (
            <div className="msg msg-agent">
              <span className="msg-sender">Asistente</span>
              <span className="msg-content typing">pensando</span>
            </div>
          )}

          <div ref={bottomRef} />
        </main>
      </div>

      {/* ── Input ─────────────────────────────────────────────────────────── */}
      <footer>
        <input
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={handleKey}
          placeholder={ready ? 'Escribe un mensaje…' : 'Inicia una sesión primero…'}
          disabled={!ready}
        />
        <button onClick={sendMessage} disabled={!ready || !input.trim()}>
          {loading ? '…' : 'Enviar'}
        </button>
      </footer>

      {/* ── Start session modal ────────────────────────────────────────────── */}
      {showStart && (
        <div className="overlay">
          <div className="modal modal-wide">
            <h2>Nueva sesión</h2>
            <p className="modal-hint">
              La colección <code>test_facts</code> se borrará en Qdrant al iniciar.
              Los recuerdos se embeben con Ollama y se recuperan semánticamente antes de cada respuesta.
            </p>

            <div className="form-section">
              <p className="section-label">Modelo de lenguaje</p>

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
            </div>

            <div className="form-section">
              <p className="section-label">Qdrant</p>
              <div className="row-2">
                <div>
                  <label>Host</label>
                  <input
                    placeholder="localhost"
                    value={draft.qdrantHost}
                    onChange={e => setDraft(d => ({ ...d, qdrantHost: e.target.value }))}
                  />
                </div>
                <div>
                  <label>Puerto</label>
                  <input
                    placeholder="6333"
                    value={draft.qdrantPort}
                    onChange={e => setDraft(d => ({ ...d, qdrantPort: e.target.value }))}
                  />
                </div>
              </div>
            </div>

            <div className="form-section">
              <p className="section-label">Ollama (embeddings)</p>
              <div className="row-2">
                <div>
                  <label>Host</label>
                  <input
                    placeholder="localhost"
                    value={draft.ollamaHost}
                    onChange={e => setDraft(d => ({ ...d, ollamaHost: e.target.value }))}
                  />
                </div>
                <div>
                  <label>Puerto</label>
                  <input
                    placeholder="11434"
                    value={draft.ollamaPort}
                    onChange={e => setDraft(d => ({ ...d, ollamaPort: e.target.value }))}
                  />
                </div>
              </div>

              <label>Modelo de embedding</label>
              <input
                placeholder="nomic-embed-text"
                value={draft.embeddingModel}
                onChange={e => setDraft(d => ({ ...d, embeddingModel: e.target.value }))}
              />
            </div>

            <div className="modal-footer">
              {conversationId && (
                <button className="btn-secondary" onClick={() => setShowStart(false)}>Cancelar</button>
              )}
              <button onClick={startSession} disabled={!draft.apiKey.trim() || starting}>
                {starting ? 'Iniciando…' : 'Iniciar sesión'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Add memory modal ──────────────────────────────────────────────── */}
      {showMemory && (
        <div className="overlay">
          <div className="modal">
            <h2>Agregar recuerdo</h2>
            <p className="modal-hint">
              El texto <em>etiqueta: valor</em> se embebe con Ollama y se almacena en Qdrant.
              El agente lo recuperará automáticamente cuando sea relevante.
            </p>

            <label>Etiqueta</label>
            <input
              placeholder="edad de Rodolfo"
              value={memDraft.label}
              autoFocus
              onChange={e => setMemDraft(d => ({ ...d, label: e.target.value }))}
              onKeyDown={handleMemKey}
            />

            <label>Valor</label>
            <input
              placeholder="35 años"
              value={memDraft.value}
              onChange={e => setMemDraft(d => ({ ...d, value: e.target.value }))}
              onKeyDown={handleMemKey}
            />

            <div className="modal-footer">
              <button className="btn-secondary" onClick={() => { setShowMemory(false); setMemDraft({ label: '', value: '' }) }}>
                Cancelar
              </button>
              <button
                onClick={saveMemory}
                disabled={!memDraft.label.trim() || !memDraft.value.trim() || savingMemory}
              >
                {savingMemory ? 'Guardando…' : 'Guardar'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
