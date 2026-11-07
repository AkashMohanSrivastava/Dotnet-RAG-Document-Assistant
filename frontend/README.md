# Document Assistant – frontend

React + TypeScript (Vite) UI for the DocumentAssistant API: upload PDFs, watch them get processed,
and ask questions with streamed, cited answers.

## Run

Start the API first (it listens on http://localhost:5056), then:

```bash
npm install
npm run dev
```

Vite proxies `/api` to the API, so no CORS configuration is needed.

## Notes

- `src/api.ts` reads the answer stream with `fetch` and parses Server-Sent Events by hand,
  because the browser's `EventSource` only supports GET and the endpoint is a POST.
- The API is stateless: the chat sends the conversation so far with every question.
- Citations like `[2]` in answers are rendered as chips that open the matching source card.
