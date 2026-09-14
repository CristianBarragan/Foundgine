# Foundgine Security / AI Pentest

This project is the security entry point for the `Foundgine.SupplyChain.Advanced` sample.

The goal is one command:

```powershell
.
\pentest\Security\Run-Security.ps1
```

It will:

1. validate Docker and .NET;
2. stop any previous Supply Chain environment;
3. build/start PostgreSQL + the Advanced Supply Chain MCP execution service;
4. wait for PostgreSQL and Foundgine readiness;
5. seed the sample database;
6. start the semantic authorization MCP API on port `4432`;
7. run the AI red-team agent against the semantic authorization API;
8. run it again against the execution MCP API;
9. write both JSON reports under `pentest/Security/artifacts`;
10. tear everything down unless `-KeepAlive` is supplied.

## Commands

Full run:

```powershell
.\pentest\Security\Run-Security.ps1
```

More aggressive bounded AI selection:

```powershell
.\pentest\Security\Run-Security.ps1 -Rounds 50
```

Keep the environment running for manual investigation:

```powershell
.\pentest\Security\Run-Security.ps1 -KeepAlive
```

Stop it later:

```powershell
.\pentest\Security\Stop-Security.ps1
```

## Architecture

```text
                    pentest/Security
                           |
                    Run-Security.ps1
                           |
             +-------------+-------------+
             |                           |
             v                           v
   Advanced Supply Chain          Semantic Auth Lab
       Docker Compose                 dotnet run
             |                           |
      +------+-------+                   |
      |              |                   |
   Postgres      MCP execution       MCP semantic
    :4429            :4422               :4432
      |                |                  |
      +----------------+------------------+
                       |
                       v
                Foundgine Red Team AI
                       |
                bounded attack catalog
                       |
                       v
                security findings
```

The LLM is optional. If `REDTEAM_MODEL_ENDPOINT` is configured, it ranks attacks from the fixed catalog. Without it, deterministic risk ordering runs the same harness.

## Safety boundary

The runner refuses non-loopback targets unless the agent is explicitly launched with `--allow-private`. The PowerShell orchestrator itself targets only the local Advanced sample.

The attack suite is deliberately focused on authorization and integrity. It avoids arbitrary command execution, persistence, credential theft, network scanning, and destructive payloads.

## The most important test

The ultimate invariant to add next is database-state verification:

```text
snapshot
   |
   v
AI attack
   |
   v
snapshot
   |
   v
unauthorized mutation == 0
```

That turns the sample from a response-level pentest into a proof that:

```text
authorization denial
        =>
no business mutation
```
