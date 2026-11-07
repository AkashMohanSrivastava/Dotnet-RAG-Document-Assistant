import { useState } from 'react'
import { ChatPanel } from './components/ChatPanel'
import { DocumentsPanel } from './components/DocumentsPanel'

export default function App() {
  // null = ask across all Ready documents
  const [documentId, setDocumentId] = useState<string | null>(null)

  return (
    <div className="app">
      <DocumentsPanel selectedId={documentId} onSelect={setDocumentId} />
      <ChatPanel documentId={documentId} />
    </div>
  )
}
