import { useEffect, useRef, useState } from 'react'
import { deleteDocument, type DocumentDto, listDocuments, reprocessDocument, uploadDocument } from '../api'

interface Props {
  selectedId: string | null
  onSelect: (id: string | null) => void
}

const isBusy = (d: DocumentDto) => d.status === 'Uploaded' || d.status === 'Processing'

export function DocumentsPanel({ selectedId, onSelect }: Props) {
  const [documents, setDocuments] = useState<DocumentDto[]>([])
  const [error, setError] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)

  const refresh = () =>
    listDocuments()
      .then(docs => { setDocuments(docs); setError(null) })
      .catch(e => setError(e.message))

  useEffect(() => { refresh() }, [])

  // Poll while anything is still processing, so status badges update by themselves.
  const anyBusy = documents.some(isBusy)
  useEffect(() => {
    if (!anyBusy) return
    const timer = setInterval(refresh, 1500)
    return () => clearInterval(timer)
  }, [anyBusy])

  async function upload(files: FileList | null) {
    if (!files?.length) return
    setUploading(true)
    try {
      for (const file of Array.from(files)) await uploadDocument(file)
      await refresh()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setUploading(false)
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  async function retry(id: string) {
    try {
      await reprocessDocument(id)
      // Show it as processing right away; the server only switches once the worker picks it up,
      // and the busy status is what starts polling.
      setDocuments(docs => docs.map(d => (d.id === id ? { ...d, status: 'Processing', failureReason: null } : d)))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  async function remove(d: DocumentDto) {
    if (!confirm(`Delete "${d.fileName}"? Its file and search data are removed permanently.`)) return
    try {
      await deleteDocument(d.id)
      if (selectedId === d.id) onSelect(null)
      setDocuments(docs => docs.filter(x => x.id !== d.id))
      // Re-read the list so it matches the server (and any earlier error message is cleared).
      await refresh()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <aside className="documents">
      <h2>Documents</h2>

      <label
        className="dropzone"
        onDragOver={e => e.preventDefault()}
        onDrop={e => { e.preventDefault(); upload(e.dataTransfer.files) }}
      >
        <input ref={fileInput} type="file" accept="application/pdf,.pdf" multiple hidden
          onChange={e => upload(e.target.files)} />
        {uploading ? 'Uploading…' : 'Drop PDFs here or click to upload'}
      </label>

      {error && <p className="error">{error}</p>}

      <ul className="document-list">
        <li>
          <button className={`document ${selectedId === null ? 'selected' : ''}`} onClick={() => onSelect(null)}>
            <span className="name">All documents</span>
          </button>
        </li>
        {documents.map(d => (
          <li key={d.id} className="document-row">
            <button
              className={`document ${selectedId === d.id ? 'selected' : ''}`}
              disabled={d.status !== 'Ready'}
              onClick={() => onSelect(d.id)}
              title={d.failureReason ?? d.fileName}
            >
              <span className="name">{d.fileName}</span>
              <span className={`status status-${d.status.toLowerCase()}`}>{d.status}</span>
            </button>
            <button
              className="delete"
              disabled={d.status === 'Processing'}
              onClick={() => remove(d)}
              title={d.status === 'Processing' ? 'Wait until processing has finished' : `Delete ${d.fileName}`}
              aria-label={`Delete ${d.fileName}`}
            >
              ×
            </button>
            {d.status === 'Failed' && (
              <div className="failure">
                <span>{d.failureReason}</span>
                <button className="link" onClick={() => retry(d.id)}>Retry</button>
              </div>
            )}
          </li>
        ))}
      </ul>
    </aside>
  )
}
