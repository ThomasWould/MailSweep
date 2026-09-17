# MailSweep

MailSweep is a privacy-focused Gmail cleanup assistant. Its first Gmail milestone connects a test account with Google OAuth and displays basic mailbox profile totals. MailSweep currently cannot modify, archive, trash, delete, label, or send email. It does not list or scan messages.

## Architecture

- `src/MailSweep.Api` — .NET 10 ASP.NET Core API. Google OAuth uses a protected local authentication cookie and `Google.Apis.Auth.AspNetCore3`; Gmail profile retrieval uses `Google.Apis.Gmail.v1`. OpenAPI JSON is available only in Development.
- `src/MailSweep.Web` — React, TypeScript, and HTTPS Vite dashboard. It checks the local session, then confirms Gmail access by loading profile totals through the API.
- `tests/MailSweep.Api.Tests` — xUnit integration tests using dummy OAuth settings and a fake Gmail profile service. Tests never contact Google.

`NuGet.Config` uses the official NuGet feed so restores do not depend on machine-specific package sources.

There is no database, message analysis, background processing, or cleanup action yet.

## Prerequisites

- .NET 10 SDK
- Node.js 24 and npm (or another Node.js version supported by the installed Vite release)
- A trusted ASP.NET Core HTTPS development certificate for both the API and Vite. See the one-time setup below.
- A Google Cloud project with the Gmail API enabled.
- A Google OAuth **Web application** client. For an External app in Testing, add your Google account as a test user. Configure the authorized redirect URI as `https://localhost:7119/signin-google`.

The only Gmail mailbox permission requested is `https://www.googleapis.com/auth/gmail.readonly`, alongside the standard `openid`, `email`, and `profile` identity scopes. The frontend runs at `https://localhost:5173`.

If the .NET 10 SDK was installed per user on Windows with `dotnet-install`, prepend it to the PowerShell session path before running the commands below:

```powershell
$env:Path = "$env:USERPROFILE\.dotnet;$env:Path"
```

## Run the API

### One-time HTTPS setup on Windows

Run these PowerShell commands once, after the .NET SDK is available on your path:

```powershell
dotnet dev-certs https --trust
New-Item -ItemType Directory -Force "$env:USERPROFILE\.aspnet\https" | Out-Null
dotnet dev-certs https --export-path "$env:USERPROFILE\.aspnet\https\mailsweep-vite.pem" --format PEM --no-password
```

Vite reads `mailsweep-vite.pem` and `mailsweep-vite.key` from that user-profile directory. The key is a local, unencrypted development key: keep the directory private and never commit or share either file. The API uses the same trusted .NET development certificate from the certificate store. If the development certificate is replaced, rerun the export command.

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

Open `https://localhost:5173`. Vite uses this exact port because the API allows credentialed requests only from that configured origin. Start the API before opening the dashboard.

The **Connect Gmail** button navigates to the API's Google authorization route. Google returns to the API callback, which creates a secure, HTTP-only, `SameSite=Lax` local session cookie and redirects to the dashboard. Both localhost apps use HTTPS, so browser requests are same-site. The dashboard calls `GET /api/auth/status` to check only the local session, then `GET /api/gmail/profile` to confirm Gmail access before showing **Connected**. The API returns only the account email address and total message and thread counts. Invalid or missing Google credentials produce a reconnect prompt; temporary Gmail failures provide a retry button. OAuth denial or failure redirects to the fixed frontend URL with only a short status code. **Disconnect** clears the local cookie; it does not revoke Google authorization. The logout endpoint requires the exact configured frontend `Origin` header.

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

There are no frontend automated tests yet. Complete the Google consent, denial, logout, and reconnect flows manually in a browser using a configured test user; automated tests do not make live OAuth or Gmail requests.

## Local configuration

Keep credentials and tokens out of the repository. The root `.gitignore` excludes `.env` files, environment-specific `appsettings` files (except the checked-in, secret-free development origin), and common credential and key file names. The checked-in launch profile contains only shared local development ports. ASP.NET Core protects the HTTP-only session cookie that the Google library uses for tokens; React never receives token values.

The backend requires `Frontend:Origin` to be an HTTPS origin without a path, query, or fragment. Development uses `https://localhost:5173` from `appsettings.Development.json`. For another environment, set `Frontend__Origin=https://app.example.test` in its private environment configuration. This single value controls the credentialed CORS allowlist, logout origin check, and fixed OAuth return destinations. Production startup fails if it is absent or invalid.

Production deployment should keep the frontend and API on the same HTTPS site or origin so the `SameSite=Lax` session cookie works as designed. A cross-site deployment requires a deliberate review of the cookie, SameSite, and CSRF architecture before use.

OAuth handler logging is suppressed because its default failure logs can contain Google's untrusted error description. The browser receives only the fixed `denied` or `failed` status.

The frontend defaults to `https://localhost:7119` for the API. To override it, copy `src/MailSweep.Web/.env.example` to `.env.local` and set `VITE_API_BASE_URL=https://api.example.test`. Vite environment values are public browser configuration; never place credentials in them.
