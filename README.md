# Prepaid Engine

Prepaid Engine is a backend service responsible for managing the end-to-end prepaid billing lifecycle for smart-meter consumers. It generates prepaid bills based on consumption and tariff data, integrates with RMS for billing and recharge processing, and manages consumer disconnection and reconnection workflows.

## Structure

- `backend/` — .NET 8 solution (`PrepaidEngine.sln`)
  - `PrepaidEngine.Api` — ASP.NET Core Web API (entry point)
  - `PrepaidEngine.Application` — use cases / business logic orchestration
  - `PrepaidEngine.Domain` — core domain models, no external dependencies
  - `PrepaidEngine.Infrastructure` — data access, external integrations (e.g. RMS)
  - `PrepaidEngine.Tests` — xUnit test project
- `frontend/` — Angular + TypeScript

## Getting Started

### Backend
```bash
cd backend
dotnet build PrepaidEngine.sln
dotnet run --project PrepaidEngine.Api
```

### Frontend
```bash
cd frontend
npm install
npm start
```
