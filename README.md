# MCP Registry Service (ASP.NET Core 8)

Minimal, ready-to-run MCP registry service implementing the v0.1 endpoints required by GitHub Copilot allowlist policy.

## Implemented endpoints

- `GET /v0.1/servers`
- `GET /v0.1/servers/{serverName}/versions/latest`
- `GET /v0.1/servers/{serverName}/versions/{version}`

Notes:

- Server names use reverse-DNS with a slash (example: `com.example/allowed-server`).
- Requests should URL-encode server names in the path (example: `com.example%2Fallowed-server`).

## CORS

This service enables CORS for all origins/methods/headers so IDE clients can fetch registry data cross-origin.

## Registry data file

Edit `Data/servers.json` to add or remove approved servers.

Server names in VS Code MCP configuration must exactly match the registry `server.name` values. Registry names must use reverse-DNS format with one slash, for example `io.github.fhanggi/koppla-active-directory`.

After changing `Data/servers.json`, restart the service so the file is loaded again.

## Local run

```bash
dotnet restore
dotnet run
```

Default URL examples (depends on launch profile):

- `http://localhost:5000/v0.1/servers`
- `https://localhost:5001/v0.1/servers`

## IIS hosting (in-process)

Prerequisites:

- .NET 8 Hosting Bundle installed on the IIS server.
- IIS site/app pool configured for **No Managed Code**.

Publish:

```bash
dotnet publish -c Release -o .\publish
```

Then point your IIS site physical path to `publish`.

## Copilot policy setting

When setting **MCP Registry URL**, use your base registry URL, for example:

- `https://registry.yourcompany.com`

Do not append `/v0.1/servers` to the configured URL.
