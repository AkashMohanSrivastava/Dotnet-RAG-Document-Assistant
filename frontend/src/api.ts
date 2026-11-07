// Typed client for the DocumentAssistant API. Shapes mirror the C# DTOs (camelCase JSON).

export type DocumentStatus = 'Uploaded' | 'Processing' | 'Ready' | 'Failed'

export interface DocumentDto {
  id: string
  fileName: string
  fileSizeBytes: number
  status: DocumentStatus
  failureReason: string | null
  uploadedAt: string
  processedAt: string | null
}

export interface AnswerSource {
  number: number
  documentId: string
  fileName: string
  pageNumber: number | null
  chunkIndex: number
  cited: boolean
  content: string
}

export interface ChatTurn {
  role: 'user' | 'assistant'
  content: string
}

export interface DoneEvent {
  answered: boolean
  citedSources: number[]
  model: string | null
  inputTokens: number | null
  outputTokens: number | null
  durationMs: number
}

export interface StreamHandlers {
  onSources: (searchQuery: string, sources: AnswerSource[]) => void
  onToken: (text: string) => void
  onDone: (done: DoneEvent) => void
}

async function ensureOk(response: Response): Promise<Response> {
  if (!response.ok) {
    // Errors come back as ProblemDetails; show its detail when there is one.
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed (${response.status})`)
  }
  return response
}

export async function listDocuments(): Promise<DocumentDto[]> {
  const response = await ensureOk(await fetch('/api/documents?pageSize=100'))
  return (await response.json()).items
}

export async function uploadDocument(file: File): Promise<DocumentDto> {
  const form = new FormData()
  form.append('file', file)
  const response = await ensureOk(await fetch('/api/documents', { method: 'POST', body: form }))
  return response.json()
}

export async function deleteDocument(id: string): Promise<void> {
  const response = await fetch(`/api/documents/${id}`, { method: 'DELETE' })
  // 404 means it's already gone (a double click, or another tab deleted it): that's what we wanted.
  if (response.status !== 404) await ensureOk(response)
}

export async function reprocessDocument(id: string): Promise<void> {
  await ensureOk(await fetch(`/api/documents/${id}/process`, { method: 'POST' }))
}

/**
 * Asks a question and streams the answer. EventSource only supports GET, so this POSTs with fetch
 * and parses the Server-Sent Events format ("event: x\ndata: {...}\n\n") from the response body.
 */
export async function askStream(
  question: string,
  history: ChatTurn[],
  documentId: string | null,
  handlers: StreamHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const response = await ensureOk(
    await fetch('/api/ask/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question, history, documentId }),
      signal,
    }),
  )

  const reader = response.body!.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += value.replace(/\r\n/g, '\n')

    // Events end with a blank line; keep any incomplete event in the buffer for the next read.
    let end: number
    while ((end = buffer.indexOf('\n\n')) >= 0) {
      dispatch(buffer.slice(0, end), handlers)
      buffer = buffer.slice(end + 2)
    }
  }
}

function dispatch(block: string, handlers: StreamHandlers) {
  let event = 'message'
  const data: string[] = []
  for (const line of block.split('\n')) {
    if (line.startsWith('event:')) event = line.slice(6).trim()
    else if (line.startsWith('data:')) data.push(line.slice(5).trimStart())
  }
  if (data.length === 0) return

  const payload = JSON.parse(data.join('\n'))
  if (event === 'sources') handlers.onSources(payload.searchQuery, payload.sources)
  else if (event === 'token') handlers.onToken(payload.text)
  else if (event === 'done') handlers.onDone(payload)
}
