# Foundgine Agent End-to-End Benchmark

This benchmark is designed to measure a complex agent interaction as a **semantic-boundary experiment**, not just as an HTTP/RPS benchmark.

It compares two agents completing the same business request against the same PostgreSQL fixture:

- **Conventional** — the agent discovers a physical application data surface and uses separate relationship/query/update tools.
- **Foundgine** — the agent uses a semantic capability, an authorized graph operation, and a semantic mutation. Physical tables, joins and SQL are outside the agent tool contract.

## Scenario

Customer 1 is reviewed across:

`Customer -> CustomerBankingRelationship -> Contract -> Transaction`

Exposure is the sum of transaction `Balance` values. If exposure is at least `48,000`, the customer is marked as reviewed by setting:

`Customer.FullName = Customer 1 Benchmark | Reviewed`

The benchmark then verifies the final state.

The fixture is the existing CoffeeBeanery PostgreSQL benchmark fixture. Customer 1 deterministically contains 4 relationships, 12 contracts and 48 transactions with the default seed settings.

## What is measured

### Agent interaction

- model calls
- tool calls
- input tokens
- output tokens
- total tokens
- cached input tokens, when the model provider reports them
- wall-clock time
- model time
- tool time

### Correctness

Both flows reset Customer 1 to the same baseline before every measured run. The benchmark then asserts that the final customer state is reviewed and that both flows operate on the same deterministic graph.

## Token accounting

`live` mode records provider-reported usage from an OpenAI-compatible chat-completions endpoint. It does **not** estimate tokens from characters.

The primary comparison is:

```text
input token saving %
= (conventional input tokens - Foundgine input tokens)
  / conventional input tokens * 100

whole interaction token saving %
= (conventional total tokens - Foundgine total tokens)
  / conventional total tokens * 100
```

Tool definitions and prior tool results are part of the model context, so they are intentionally included in the provider-reported input-token count.

## Run it from the repository root

There is now a runner script. `publish-report.ps1` only publishes an existing JSON report; it does **not** run the benchmark.

Replay/correctness harness (starts PostgreSQL + Foundgine warm API automatically):

```powershell
.\run-agent-benchmark.ps1
```

Or run the benchmark script directly:

```powershell
.\benchmarks\AgentEndToEnd\Run1\run-agent-benchmark.ps1 -Mode replay -Warmups 1 -Runs 3 -Publish
```

For real token measurements, use live mode and provide an OpenAI-compatible endpoint, API key and model:

```powershell
$env:AGENT_MODEL_ENDPOINT = "https://your-compatible-endpoint/v1/chat/completions"
$env:AGENT_MODEL_API_KEY = "..."
$env:AGENT_MODEL = "your-model"
.\run-agent-benchmark.ps1 -Mode live -Warmups 5 -Runs 30 -Publish
```

The runner uses the benchmark compose fixture by default:
- PostgreSQL: `localhost:55432`
- Foundgine warm GraphQL: `http://localhost:4302/graphql/warm`

Use `-KeepInfrastructure` when running several experiments against the same containers.

## Modes

### Replay mode

Replay validates the tool choreography and final-state harness without requiring a model endpoint. **Replay does not produce real model token evidence and must not be used for token-savings claims.**

```powershell
$env:BankingConnectionString = "Host=localhost;Port=5432;Database=...;Username=...;Password=..."
$env:FOUNDGINE_GRAPHQL_URL = "http://localhost:8080/graphql/warm"
$env:AGENT_BENCHMARK_MODE = "replay"

dotnet run --project src/csharp/benchmarks/AgentEndToEnd/Run1/Foundgine.AgentEndToEnd.Run1.csproj
```

### Live mode

Point the benchmark at an OpenAI-compatible endpoint. The endpoint can be a hosted model service or a local compatible gateway.

```powershell
$env:BankingConnectionString = "Host=localhost;Port=5432;Database=...;Username=...;Password=..."
$env:FOUNDGINE_GRAPHQL_URL = "http://localhost:8080/graphql/warm"
$env:AGENT_BENCHMARK_MODE = "live"
$env:AGENT_MODEL_ENDPOINT = "https://your-compatible-endpoint/v1/chat/completions"
$env:AGENT_MODEL_API_KEY = "..."
$env:AGENT_MODEL = "your-model"
$env:AGENT_BENCHMARK_WARMUPS = "5"
$env:AGENT_BENCHMARK_RUNS = "30"

dotnet run --project src/csharp/benchmarks/AgentEndToEnd/Run1/Foundgine.AgentEndToEnd.Run1.csproj
```

The benchmark writes:

- `artifacts/agent-benchmark/agent-benchmark.json`
- `artifacts/agent-benchmark/agent-benchmark.md`

## Experimental discipline

For publishable evidence, run the same model, temperature, system prompt, user request, database fixture, model endpoint and network environment for both flows.

Recommended:

1. 5 warm-up runs per flow.
2. 30 measured runs per flow.
3. 3 independent benchmark sessions.
4. Reset the database state before every measured run.
5. Keep model and endpoint configuration identical.
6. Publish provider-reported token usage and the exact benchmark report.
7. Treat token savings and latency separately; a lower token count does not automatically imply lower wall-clock time.

## Important limitation

This benchmark currently measures a real Foundgine graph/mutation path but uses the existing CoffeeBeanery benchmark domain. It is intentionally **not** presented as proof of tenant authorization or financial-transfer safety. A separate security scenario should be added before making those claims.
