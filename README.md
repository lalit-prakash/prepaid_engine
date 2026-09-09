# Prepaid Engine

Prepaid Engine is a backend service responsible for managing the end-to-end prepaid billing lifecycle for smart-meter consumers. It generates prepaid bills based on consumption and tariff data, integrates with RMS for billing and recharge processing, and manages consumer disconnection and reconnection workflows.

## Structure

- `backend/` — ASP.NET Core Web API (C#)
- `frontend/` — Angular + TypeScript

## Getting Started

### Backend
```bash
cd backend/PrepaidEngine.Api
dotnet run
```

### Frontend
```bash
cd frontend
npm install
npm start
```
