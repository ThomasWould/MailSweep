# MailSweep

MailSweep is a privacy-focused Gmail cleanup assistant. The planned product will help people understand mailbox storage and clutter, review cleanup candidates, and choose what to archive or move to Trash. This repository currently contains only the application foundation. It does not connect to Gmail or modify any messages.

## Architecture

- `src/MailSweep.Api` — .NET 10 ASP.NET Core API with a typed `GET /api/health` endpoint. OpenAPI JSON is available only in Development.
- `src/MailSweep.Web` — React, TypeScript, and Vite dashboard shell. It displays placeholder connection and mailbox statistics states; it makes no API requests.
- `tests/MailSweep.Api.Tests` — xUnit integration test for the health endpoint.

`NuGet.Config` uses the official NuGet feed so restores do not depend on machine-specific package sources.

There is no OAuth flow, Gmail API client, database, analysis, or cleanup action yet.

## Prerequisites

- .NET 10 SDK
- Node.js 24 and npm (or another Node.js version supported by the installed Vite release)
- A trusted ASP.NET Core HTTPS development certificate for browser access. On first setup, run `dotnet dev-certs https --trust`.

If the .NET 10 SDK was installed per user on Windows with `dotnet-install`, prepend it to the PowerShell session path before running the commands below:

```powershell
$env:Path = "$env:USERPROFILE\.dotnet;$env:Path"
```

## Run the API

From the repository root:

```sh
dotnet restore MailSweep.sln
dotnet run --project src/MailSweep.Api --launch-profile https
```

The health endpoint is at `https://localhost:7119/api/health`. In Development, the OpenAPI document is at `https://localhost:7119/openapi/v1.json`. The HTTP listener on port 5158 redirects to HTTPS.

## Run the frontend

In a separate terminal:

```sh
cd src/MailSweep.Web
npm ci
npm run dev
```

Open the local URL printed by Vite (normally `http://localhost:5173`). The dashboard is a static placeholder and does not require the API to be running.

## Build and test

From the repository root:

```sh
dotnet build MailSweep.sln
dotnet test MailSweep.sln
```

For the frontend:

```sh
cd src/MailSweep.Web
npm run build
npm run lint
```

There are no frontend tests yet because the shell has no interactive behavior. Vitest can be added when frontend logic is introduced.

## Local configuration

Keep future credentials and tokens out of the repository. The root `.gitignore` excludes `.env` files, environment-specific `appsettings` files, and common credential and key file names. The checked-in launch profile contains only shared local development ports.
