# MailSweep

MailSweep is a privacy-focused Gmail cleanup assistant. Its first Gmail milestone connects a test account with Google OAuth and displays basic mailbox profile totals. MailSweep currently cannot modify, archive, trash, delete, label, or send email. It does not list or scan messages.

## Architecture

- `src/MailSweep.Api` — .NET 10 ASP.NET Core API. Google OAuth uses a protected local authentication cookie and `Google.Apis.Auth.AspNetCore3`; Gmail profile retrieval uses `Google.Apis.Gmail.v1`. OpenAPI JSON is available only in Development.
- `src/MailSweep.Web` — React, TypeScript, and Vite dashboard. It checks connection status and displays Gmail profile totals through the API.
- `tests/MailSweep.Api.Tests` — xUnit integration tests using dummy OAuth settings and a fake Gmail profile service. Tests never contact Google.

`NuGet.Config` uses the official NuGet feed so restores do not depend on machine-specific package sources.

There is no database, message analysis, background processing, or cleanup action yet.

## Prerequisites

- .NET 10 SDK
- Node.js 24 and npm (or another Node.js version supported by the installed Vite release)
- A trusted ASP.NET Core HTTPS development certificate for browser access. On first setup, run `dotnet dev-certs https --trust`.
- A Google Cloud project with the Gmail API enabled.
- A Google OAuth **Web application** client. For an External app in Testing, add your Google account as a test user. Configure the authorized redirect URI as `https://localhost:7119/signin-google`.

The only Gmail mailbox permission requested is `https://www.googleapis.com/auth/gmail.readonly`, alongside the standard `openid`, `email`, and `profile` identity scopes. The frontend runs at `http://localhost:5173`.

If the .NET 10 SDK was installed per user on Windows with `dotnet-install`, prepend it to the PowerShell session path before running the commands below:

```powershell
$env:Path = "$env:USERPROFILE\.dotnet;$env:Path"
```

## Run the API

The API reads the OAuth client ID and secret from configuration. Store development values in .NET user-secrets, outside this repository. The commands below show placeholders only; enter real values in a private local terminal and avoid leaving them in shell history.

```sh
dotnet user-secrets init --project src/MailSweep.Api
dotnet user-secrets set "Authentication:Google:ClientId" "<GOOGLE_CLIENT_ID>" --project src/MailSweep.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<GOOGLE_CLIENT_SECRET>" --project src/MailSweep.Api
```

The project is already initialized for user-secrets, so `init` is only needed if that ID is missing. From the repository root:

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

Open `http://localhost:5173`. Vite uses this exact port because the API allows credentialed requests only from that origin. Start the API before opening the dashboard.

The **Connect Gmail** button navigates to the API's Google authorization route. Google returns to the API callback, which creates a secure, HTTP-only local session cookie and redirects to the dashboard. The dashboard calls `GET /api/auth/status`, then `GET /api/gmail/profile` when connected. The API returns only the account email address and total message and thread counts. **Disconnect** clears the local cookie; it does not revoke Google authorization.

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

There are no frontend automated tests yet. Complete the Google consent flow manually in a browser using a configured test user; automated tests do not make live OAuth or Gmail requests.

## Local configuration

Keep credentials and tokens out of the repository. The root `.gitignore` excludes `.env` files, environment-specific `appsettings` files, and common credential and key file names. The checked-in launch profile contains only shared local development ports. ASP.NET Core protects the HTTP-only session cookie that the Google library uses for tokens; React never receives token values.
