import { Fragment, useEffect, useRef, useState } from 'react'
import { type AnswerSource, askStream, type ChatTurn, type DoneEvent } from '../api'

interface Message {
  role: 'user' | 'assistant'
  content: string
  searchQuery?: string
  sources?: AnswerSource[]
  done?: DoneEvent
  error?: string
}

interface Props {
  documentId: string | null
}

const EXAMPLES = [
  'Summarize the key points of this document.',
  'What are the termination conditions?',
  'Which law governs this agreement?',
]

export function ChatPanel({ documentId }: Props) {
  const [messages, setMessages] = useState<Message[]>([])
  const [input, setInput] = useState('')
  const [streaming, setStreaming] = useState(false)
  const [highlight, setHighlight] = useState<string | null>(null) // "<message index>-<source number>"
  const list = useRef<HTMLDivElement>(null)

  // Scroll only the message list (scrollIntoView would also scroll the page itself).
  useEffect(() => {
    list.current?.scrollTo({ top: list.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  // Updates the assistant message currently being streamed (always the last one).
  const updateLast = (change: (m: Message) => Message) =>
    setMessages(ms => [...ms.slice(0, -1), change(ms[ms.length - 1])])

  async function ask(question: string) {
    question = question.trim()
    if (!question || streaming) return

    // The server is stateless: send the finished conversation so far with every question.
    const history: ChatTurn[] = messages
      .filter(m => !m.error)
      .map(m => ({ role: m.role, content: m.content }))

    setMessages(ms => [...ms, { role: 'user', content: question }, { role: 'assistant', content: '' }])
    setInput('')
    setStreaming(true)
    try {
      await askStream(question, history, documentId, {
        onSources: (searchQuery, sources) => updateLast(m => ({ ...m, searchQuery, sources })),
        onToken: text => updateLast(m => ({ ...m, content: m.content + text })),
        onDone: done => updateLast(m => ({
          ...m,
          done,
          sources: m.sources?.map(s => ({ ...s, cited: done.citedSources.includes(s.number) })),
        })),
      })
    } catch (e) {
      updateLast(m => ({ ...m, error: (e as Error).message }))
    } finally {
      setStreaming(false)
    }
  }

  function showSource(messageIndex: number, number: number) {
    const key = `${messageIndex}-${number}`
    setHighlight(key)
    document.getElementById(`source-${key}`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
  }

  return (
    <main className="chat">
      <div className="messages" ref={list}>
        {messages.length === 0 && (
          <div className="empty">
            <h1>Ask your documents</h1>
            <p>Answers are generated only from your uploaded PDFs, with citations to the exact pages.</p>
            <div className="examples">
              {EXAMPLES.map(e => <button key={e} onClick={() => ask(e)}>{e}</button>)}
            </div>
          </div>
        )}

        {messages.map((m, i) => m.role === 'user'
          ? <div key={i} className="message user">{m.content}</div>
          : (
            <div key={i} className="message assistant">
              {m.searchQuery && messages[i - 1]?.content !== m.searchQuery && (
                <div className="search-query">Searched for: “{m.searchQuery}”</div>
              )}

              <div className="answer">
                <AnswerText text={m.content} onCite={n => showSource(i, n)} />
                {streaming && i === messages.length - 1 && <span className="cursor" />}
              </div>
              {m.error && <p className="error">{m.error}</p>}

              {m.sources && m.sources.length > 0 && (
                <div className="sources">
                  {m.sources.map(s => {
                    const key = `${i}-${s.number}`
                    return (
                      <details key={key} id={`source-${key}`}
                        className={`source ${s.cited ? 'cited' : ''} ${highlight === key ? 'highlight' : ''}`}
                        open={highlight === key}>
                        <summary>
                          <span className="number">{s.number}</span>
                          <span className="file">{s.fileName}</span>
                          {s.pageNumber && <span className="page">p. {s.pageNumber}</span>}
                        </summary>
                        <p>{s.content}</p>
                      </details>
                    )
                  })}
                </div>
              )}

              {m.done && (
                <div className="stats">
                  {m.done.model} · {m.done.inputTokens ?? '?'} in / {m.done.outputTokens ?? '?'} out tokens · {(m.done.durationMs / 1000).toFixed(1)}s
                </div>
              )}
            </div>
          ))}
      </div>

      <form className="composer" onSubmit={e => { e.preventDefault(); ask(input) }}>
        <input value={input} onChange={e => setInput(e.target.value)} disabled={streaming}
          placeholder={documentId ? 'Ask about the selected document…' : 'Ask about all documents…'} />
        <button type="submit" disabled={streaming || !input.trim()}>{streaming ? '…' : 'Ask'}</button>
      </form>
    </main>
  )
}

/**
 * Renders the answer with each [n] citation as a clickable chip that reveals source n.
 * Markdown list markers ("* " / "- ") become bullets; that's all the Markdown these answers use.
 */
function AnswerText({ text, onCite }: { text: string; onCite: (n: number) => void }) {
  const withBullets = text.replace(/^[ \t]*[*-][ \t]+/gm, '• ')
  return (
    <>
      {withBullets.split(/(\[\d+\])/).map((part, i) => {
        const match = /^\[(\d+)\]$/.exec(part)
        return match
          ? <button key={i} className="citation" onClick={() => onCite(Number(match[1]))}>{match[1]}</button>
          : <Fragment key={i}>{part}</Fragment>
      })}
    </>
  )
}
