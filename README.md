# CX Insight: customer-satisfaction platform with an Agentic RAG assistant

Car dealers (think Honda North America) get survey scores, a rank and an AI assistant that answers questions such as
*"What is my score this quarter?"*, *"What is my rank?"* and *"How can I improve my score?"* from live data and policy documents.

| Layer | Tech |
|---|---|
| Frontend | Angular 22 (standalone, signals, zoneless), Chart.js, marked |
| Backend | .NET 10 minimal APIs, JWT auth, ProblemDetails, health checks, rate limiting, OpenTelemetry |
| Database | MongoDB (`cx` database, local) |
| AI | Microsoft.Extensions.AI (`IChatClient`, `IEmbeddingGenerator`), Ollama by default (**gemma4:e4b**), MCP tools |

## Quick start

Prerequisites: .NET 10 SDK, Node 24, MongoDB on `localhost:27017`, Ollama on `localhost:11434`.

```bash
# 1. models (Gemma 4 chats and calls tools but cannot embed, so RAG needs an embedding model)
ollama pull gemma4:e4b
ollama pull embeddinggemma

# 2. (optional in Development) persist the secret used to sign login tokens; kept out of the repo
dotnet user-secrets set "Jwt:SigningKey" "<any random string of 32+ characters>" --project backend/src/Cx.Api
#    User-secrets load ONLY when ASPNETCORE_ENVIRONMENT=Development. `dotnet run` and the IDE's "http" launch profile set it;
#    starting bin/.../Cx.Api.exe directly does not. In Development a missing key falls back to a temporary random one;
#    in any other environment set the Jwt__SigningKey environment variable (32+ characters) or startup fails with a clear message.

# 3. seed MongoDB: 10 dealers, 1000 customers, 1000 survey responses, users, and the embedded policy chunks
dotnet run --project backend/src/Cx.Seeder            # add --reset to regenerate, --reindex to re-embed policies

# 4. run
dotnet run --project backend/src/Cx.Api               # http://localhost:5080
cd frontend && npm install && npm start               # http://localhost:4200 (proxies /api to :5080)
```

The first AI question can take a minute while Ollama loads the model into memory; later ones take a few seconds.

### Pointing the frontend at an API

The API address is `apiBaseUrl` in `frontend/src/environments/`: `environment.development.ts` is used by `ng serve`, `environment.ts` by `ng build`.
Empty means same origin: in development the dev server's `proxy.conf.json` forwards `/api` to `http://localhost:5080` (change its `target` to use another API);
in production a reverse proxy is expected to do the same. To call an API on another origin, set for example `apiBaseUrl: 'https://cx-api.example.com'` and add the
app's origin to `Cors:AllowedOrigins` in `backend/src/Cx.Api/appsettings.json`. The value is applied in one place (`core/api-config.ts`), not at each call site.

### Debugging the MCP server

The API starts `Cx.McpServer` as a child process (first AI question per user; idle ones stop after 15 minutes), so F5 on that project is not the way in.
Start the API with the **`http (debug MCP server)`** launch profile, or set `CX_MCP_DEBUG` in the API's environment (children inherit it):

- `CX_MCP_DEBUG=1`: the new process calls `Debugger.Launch()`; choose your open Visual Studio instance and breakpoints in `CxTools.cs` etc. hit.
- `CX_MCP_DEBUG=wait`: it prints its PID to stderr and waits up to 2 minutes for **Debug > Attach to Process**.

The flag is compiled out of Release builds. While you are paused, the API's call may time out. The server's stderr appears in the API log at Debug level for the
`Cx.Ai.Agent.McpToolProvider` category.

### Demo logins (plain-text passwords by request; produced by the seeder, which is deterministic)

| Username | Password | Sees |
|---|---|---|
| `admin` | `Falcon-Pioneer-26` | all dealers, the leaderboard, dealer names |
| `dealer01` | `Quartz-Juniper-64` | HND-1001 Northgate Honda |
| `dealer02` | `Zephyr-Quartz-26` | HND-1002 Riverbend Honda |
| `dealer03` | `Meadow-Quartz-87` | HND-1003 Summit Honda |
| `dealer04` | `Cedar-Nimbus-60` | HND-1004 Harborview Honda |
| `dealer05` | `Harbor-Pioneer-59` | HND-1005 Prairie Wind Honda |
| `dealer06` | `Nimbus-Maple-81` | HND-1006 Sunbelt Honda |
| `dealer07` | `Orchid-Comet-49` | HND-1007 Lakeshore Honda |
| `dealer08` | `Aurora-Cobalt-22` | HND-1008 Redwood Honda |
| `dealer09` | `Juniper-Marlin-53` | HND-1009 Capital City Honda |
| `dealer10` | `Basalt-Maple-43` | HND-1010 Bayou Honda |

## What the app does

**Scoring** (`Cx.Core/Scoring`, documented for the AI in `Cx.Core/Policies`)

- Q1 *recommend*: Highly recommend 100 / Recommend 50 / Might recommend 25 / Not recommend 0.
- Q2 *missing part or defect*: All good 100 / Part missing 50 / Defect 50 / Both 0.
- Net score of a survey = average of the two. **Dealer score for a period = total net score ÷ number of surveys.**
- **Rank**: dealers ordered by score for the same period; ties share a rank (1, 1, 3); dealers with no surveys are unranked.

**Pages** (all share one date filter: presets or an explicit start and end; the default is the current month)

- Login
- Dashboard: score, rank, surveys, benchmark, trend, answer breakdowns and the **Ask AI** button
- Survey responses: filterable, sortable, paged
- Leaderboard (admin only): ranked dealers; "Open dashboard" drills into one dealer

**MongoDB collections** (`cx`): `dealers`, `customers`, `surveyResponses`, `users`, `policyChunks` (text **and embedding vectors**), `conversations`.

## How the Agentic RAG works

```
Browser ──SSE──> POST /api/ai/chat ──> AgentService ──> IChatClient pipeline
                                          │               FunctionInvokingChatClient (the agent loop)
                                          │                 -> logging -> OpenTelemetry -> provider adapter (Ollama / OpenAI / Anthropic / Google)
                                          │
                                          └─ tools over MCP (stdio) ──> Cx.McpServer (one process per data scope)
                                                 get_dealer_score · get_dealer_rank · get_top_ranked_score
                                                 get_score_trend · get_leaderboard (admin) · search_policy
                                                                                           │
                                             policyChunks in MongoDB (text + 768-d vectors) ┘  cosine search
```

The model is not told which tools to use. It reads the question and decides. For *"How can I improve my score?"* it typically
calls `get_dealer_score`, notices that the recommend average is lower than the vehicle-condition average, then calls
`search_policy` about that weakness and writes advice that cites the real numbers and the policy. The UI shows every tool step.

- **Retrieval**: policy markdown is split by heading, embedded in batches and stored in MongoDB with the embedding model name; re-indexing skips unchanged
  chunks and switching the embedding model re-embeds everything (vectors from different models are never mixed). A community `mongod` has no `$vectorSearch`,
  so the vectors are loaded into `Microsoft.Extensions.VectorData`'s in-memory collection for exact cosine search (reloaded automatically when chunks change).
  On Atlas or MongoDB with `mongot`, swap `PolicySearch` for a `$vectorSearch` implementation.
- **Model choice**: the panel lists every Ollama model that supports tool calling (auto-discovered) plus hosted models when a key is configured.
  A conversation cannot change provider midway (reasoning state is provider-specific); changing the provider starts a new chat.
- **Failure handling**: a turn is saved only if it finishes with a text answer and no dangling tool call, so a failed or truncated model call never corrupts history.

### Using other models

Edit `backend/cx.settings.json` (`Ai:Default`, `Ai:*:Models`) and supply keys through the environment or user-secrets, never the repo:

```bash
dotnet user-secrets set "Ai:Anthropic:ApiKey" "<key>" --project backend/src/Cx.Api     # or env var Ai__Anthropic__ApiKey
# also: Ai:OpenAI:ApiKey (Ai:OpenAI:Endpoint for LM Studio / vLLM / OpenRouter), Ai:Google:ApiKey
```

## Security model

Dealer isolation is enforced in three independent places, none of which trusts the model or the browser:

1. **API**: the data scope comes only from the validated JWT (`DataScope`). A dealer passing another `dealerId` gets `403`; `/api/leaderboard` is admin-only.
2. **Service layer**: every `ScoreService` read resolves the dealer through `DataScope.ResolveDealer`.
3. **MCP server**: each process is started with a fixed scope in its environment, refuses to start without one, and the tools have no "which dealer" argument a prompt could change.
   Dealers see the top score as a benchmark but never the top dealer's name.

Also: login is rate-limited and identical for unknown user / wrong password; secrets only from user-secrets or environment; CORS origins from config.

## Tests

```bash
dotnet test backend/Cx.slnx          # 80 tests; database tests use a throw-away cx_test_<guid> database and skip if MongoDB is down
cd frontend && npm test              # 16 tests
```

No test calls a live LLM: the agent loop runs the real `FunctionInvokingChatClient` against a scripted fake model, retrieval uses a deterministic hash embedder,
and one suite starts the real built MCP server process.

## Layout

```
backend/
  cx.settings.json        shared Mongo / AI / MCP settings (lowest priority; env vars and user-secrets override)
  Directory.Packages.props  every package version, in one place
  src/Cx.Core/            domain, scoring, MongoDB, policy chunking / ingestion / search, demo data
  src/Cx.Ai/              provider factory, model catalog, MCP client pool, agent loop
  src/Cx.McpServer/       MCP tools (stdio)
  src/Cx.Api/             minimal API
  src/Cx.Seeder/          seeds data and indexes policies
  tests/Cx.Tests/
frontend/                 Angular app
```

## Before production

- Passwords are stored in plain text on purpose for this demo. Replace with a salted hash (`PasswordHasher<T>`).
- Put the API behind HTTPS and a reverse proxy; the dev proxy is in `frontend/proxy.conf.json`.
- Dockerfiles and CI are not included.
- Hosted-provider adapters (OpenAI, Anthropic, Google) compile and are wired, but were not exercised without API keys. Model names in `cx.settings.json` are yours to adjust.
