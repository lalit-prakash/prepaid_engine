# Frontend

Angular 22 operations UI for the Prepaid Engine. Standalone components, lazy-routed pages, signals for
local state, plain SCSS design tokens (`src/styles/_tokens.scss`), no UI library.

```bash
npm install
npm start        # http://localhost:4200 (expects the API on http://localhost:5043)
npm run build    # production build into dist/
npm test         # Vitest (currently a scaffold spec only)
```

The API base URL is `src/environments/environment.ts`. Sign in with a demo user configured on the
backend (see the repository README).

Structure and conventions are described in [`docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §6:
`core/` (API contracts and services), `shared/` (icon, status badge, KPI card, bar chart, utilities),
`layouts/shell/`, and `features/<module>/pages/`. List pages use server-side search and keyset paging;
a page shows "Data unavailable" rather than an invented number.
