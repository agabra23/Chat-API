# Chat-API

A modular, locally-runnable chat API built on **.NET 10**. It does multi-turn chat, gives the model a web search **tool** it can invoke when it decides live information is needed, and lets you swap the model backend per request — local Ollama by default, or any cloud model (Claude Opus, GPT, OpenRouter, Groq, …).

## Requirements

- .NET 10 SDK (pinned via `global.json` to `10.0.401`)
- [Ollama](https://ollama.com) running locally, with a **tool-capable** model pulled

## Run

```bash
dotnet run --project src/ChatApi/ChatApi.csproj
```

Listens on `http://localhost:5080`.

## Endpoints

| Method   | Route                 | Purpose                           |
| -------- | --------------------- | --------------------------------- |
| `POST`   | `/chat`               | Send a message, get a reply       |
| `GET`    | `/providers`          | List registered model providers   |
| `GET`    | `/tools`              | List tools available to the model |
| `GET`    | `/conversations/{id}` | Inspect stored history            |
| `DELETE` | `/conversations/{id}` | Clear a conversation              |
| `GET`    | `/health`             | Liveness check                    |

## Basic chat

```bash
curl -X POST http://localhost:5080/chat \
  -H 'Content-Type: application/json' \
  -d '{"message":"Who is the current CEO of OpenAI?"}'
```

The response includes a `conversationId`. Pass it back to continue the conversation:

```bash
curl -X POST http://localhost:5080/chat \
  -H 'Content-Type: application/json' \
  -d '{"message":"And who was before him?","conversationId":"<id from above>"}'
```

## Request fields

All fields except `message` are optional.

| Field                      | Description                                                    |
| -------------------------- | -------------------------------------------------------------- |
| `message`                  | The user's message (required)                                  |
| `conversationId`           | Omit to start a new conversation                               |
| `provider`                 | `ollama` (default), `anthropic`, `openai`, `openai-compatible` |
| `model`                    | Model id; defaults to the provider's configured model          |
| `systemPrompt`             | Applied on the first turn of a conversation                    |
| `enableTools`              | Set `false` to withhold all tools for this request             |
| `tools`                    | Restrict which tools are offered, e.g. `["web_search"]`        |
| `temperature`, `maxTokens` | Sampling controls                                              |
| `apiKey`                   | Per-request key, so no secrets need to live in config          |
| `baseUrl`                  | Override the provider endpoint (e.g. a remote Ollama host)     |

## Using a cloud model

Set the key via environment variable and pick the provider:

```bash
export ANTHROPIC_API_KEY=sk-ant-...

curl -X POST http://localhost:5080/chat \
  -H 'Content-Type: application/json' \
  -d '{"message":"Explain CRDTs","provider":"anthropic","model":"claude-opus-4-1"}'
```

Or pass the key inline per request:

```bash
curl -X POST http://localhost:5080/chat \
  -H 'Content-Type: application/json' \
  -d '{"message":"Hi","provider":"openai","model":"gpt-4o","apiKey":"sk-..."}'
```

`openai-compatible` targets any OpenAI-shaped `/chat/completions` endpoint — OpenRouter, Groq, Together, vLLM, LM Studio — just set `baseUrl`.

## Tools & web grounding

Grounding is a **tool the model calls**, not something applied to every turn. Each request advertises the available tools; the model decides whether to invoke one, writes its own search query, and the API runs the tool and feeds results back. This repeats until the model produces a final answer (capped by `Tools:MaxIterations`).

```
"What is 17 * 23?"                 -> no tool call, answers directly
"Newest features in .NET 10?"      -> calls web_search{query: ".NET 10 release notes new features"}
```

The response reports what was invoked:

```json
{
  "message": "...",
  "toolCalls": [{ "name": "web_search", "arguments": { "query": "..." } }],
  "sources": [{ "index": 1, "title": "...", "url": "..." }]
}
```

Tool execution is fault-tolerant: failures are returned to the model as text so it can recover or answer without the tool, rather than failing the request.

Search backends for `web_search`, selected via `Search:Provider`:

- `duckduckgo` — default, no API key
- `searxng` — point `Search:SearxngBaseUrl` at your instance
- `tavily` — set `TAVILY_API_KEY`

Disable tools globally with `Tools:Enabled: false`, or per request with `"enableTools": false`.

> Tool calling requires a model that supports it. Check with `ollama show <model>` — it should list `tools` under capabilities.

## Architecture

```
src/ChatApi/
├── Abstractions/     Interfaces: IChatProvider, ITool, IToolRegistry,
│                     IWebSearchProvider, IConversationStore
├── Providers/        Ollama, Anthropic, OpenAI-compatible + registry
├── Tools/            WebSearchTool + tool registry
├── Grounding/        DuckDuckGo, SearXNG, Tavily search backends
├── Conversations/    In-memory history store
├── Services/         ChatService — the tool-calling loop
├── Endpoints/        Minimal API route definitions
├── Contracts/        Request/response DTOs
└── Extensions/       DI wiring
```

Everything sits behind an interface, so extending is additive:

- **New tool** — implement `ITool` (name, description, JSON Schema, `ExecuteAsync`) and register it as `ITool`. The model can use it immediately; no changes to `ChatService`.
- **New model backend** — implement `IChatProvider` and register it as `IChatProvider`. The registry picks it up by `Name` automatically.
- **New search backend** — implement `IWebSearchProvider`, register it, set `Search:Provider` to its name.
- **Durable history** — implement `IConversationStore` (Redis, Postgres) and replace the in-memory registration.

Tool calls and their results are persisted alongside the conversation, since providers require a tool call and its result to stay paired in history.

## Note

History is in-memory and resets when the process restarts.
