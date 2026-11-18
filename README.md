# Swarnabhoomi

Swarnabhoomi is a rice-network dashboard with a React frontend and an ASP.NET Core API.

## Run locally

In Development, the API stores data in a local SQLite database at `backend\src\SWARNABHOOMI.Api\App_Data\swarnabhoomi.db`, so registrations and other changes survive API restarts. The database file is local and ignored by Git. Outside Development, configure `ConnectionStrings:DefaultConnection` with PostgreSQL; without it, the API uses an in-memory database.

Set a local administrator account using .NET user-secrets. Choose your own strong password of at least 12 characters; do not put it in source control:

```powershell
dotnet user-secrets set --project backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj "Authentication:BootstrapAdmin:Email" "admin@swarnabhoomi.lk"
dotnet user-secrets set --project backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj "Authentication:BootstrapAdmin:Password" "<your-unique-password>"
dotnet user-secrets set --project backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj "Authentication:BootstrapAdmin:FirstName" "System"
dotnet user-secrets set --project backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj "Authentication:BootstrapAdmin:LastName" "Administrator"
```

Start the API and frontend together:

```powershell
cd frontend
npm install
npm run dev
```

The `dev` command starts the API in Development (loading .NET user-secrets and local SQLite), waits for its health check on port `5025`, and then starts Vite. Open the local URL printed by Vite. To run only the frontend, use `npm run dev:frontend`; the API must then be started separately with:

```powershell
dotnet run --project ..\backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj --urls http://localhost:5025
```

The seeded farmer and buyer accounts do not have default passwords. Sign in with the bootstrap administrator credentials configured in .NET user-secrets, or register an account and have an administrator approve it.

To enable AI replies for all signed-in members, configure your Gemini API key locally with .NET user-secrets. Do not put the key in source control or share it in chat:

```powershell
dotnet user-secrets set --project backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj "AiAssistant:ApiKey" "<your-gemini-api-key>"
dotnet user-secrets set --project backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj "AiAssistant:BaseUrl" "https://generativelanguage.googleapis.com/v1beta/openai/"
dotnet user-secrets set --project backend\src\SWARNABHOOMI.Api\SWARNABHOOMI.Api.csproj "AiAssistant:Model" "gemini-4-hydrogen"
```

The Gemini endpoint and model above are the defaults. Alternatively, a Super Administrator can open **AI settings**, choose **Use Gemini settings**, paste the key into the password field, enable the assistant, and save. The key is encrypted on the server and never returned to the browser. Do not paste it into chat or source files. Restart the API after changing user-secrets. For deployment, provide `AiAssistant__ApiKey`, `AiAssistant__BaseUrl`, and `AiAssistant__Model` through the hosting platform's secret manager/environment. The assistant also supports other OpenAI-compatible providers by changing the base URL and model.
## Authentication

The API hashes passwords with ASP.NET Core Identity's password hasher, issues an HttpOnly authentication cookie, rate-limits sign-in and registration attempts, and protects dashboard data endpoints. People can sign up as a farmer, buyer, or government user; new accounts remain inactive until an administrator approves them in the **People** section. Rejected requests are removed. A SuperAdmin can promote trusted active members to **Administrator** (Developer) from that section; public registration cannot grant administrative roles.

The bootstrap administrator is created or updated from the user-secret settings above each time the API starts. Never use a shared or publicly disclosed password for a real deployment; change it to a unique password and store production credentials in a secret manager.

## Field map and AI assistant

The **Paddy field map** is available to farmers, buyers, government users, officers, and Super Administrators. Farmers see and register their own farms and draw field boundaries; officers see fields in assigned districts and government users can review authorized-area fields. Super Administrators can see all field boundaries. Buyers receive district-level verified-supply totals rather than field boundaries or member identities. Map users can mark a polygon and copy its GeoJSON coordinates; farmers can submit field boundaries for verification, and the server calculates their area. Map tiles are provided by OpenStreetMap; keep the visible attribution in the map.

All signed-in members can use the **AI assistant** for questions about platform workflows and general rice-network topics. Configure Gemini or another OpenAI-compatible provider through server secrets/environment or **AI settings**. API keys configured in **AI settings** are encrypted server-side and never sent back to the browser. The assistant cannot access private member records or perform site actions; it does not replace authorized agronomic advice, and chats are not persisted by the application.

For production, configure `ConnectionStrings:DefaultConnection` with a PostgreSQL connection string, run the API over HTTPS, and persist and protect the ASP.NET Core Data Protection key ring so the stored provider key remains decryptable across deployments. Store production credentials in a secret manager, not in this repository.

## Live weather and rice prices

The **Weather** page requests current model conditions for representative coordinates in all 25 Sri Lankan districts from Open-Meteo and refreshes every 15 minutes. These are model-based estimates, not readings from a station. The page links to Open-Meteo and the CC BY 4.0 licence and attributes the data. Open-Meteo's free API is for non-commercial use only; commercial deployments must use a paid plan or another authorized provider.

The **Market prices** page checks the Central Bank of Sri Lanka's Daily Price Report page hourly and displays the newest report PDF, including its rice price tables for the markets CBSL covers. CBSL publishes daily reports, not continuous live trading quotes; if a report for today is not yet available, the page clearly shows the latest published report date.


hashed_pw_123_kkp!1
samankumara@gmail.com


lalkantha_12345_11!
lalkantha@gmail.com

nimaltissa_12345_11!
nimaltissa@gmail.com