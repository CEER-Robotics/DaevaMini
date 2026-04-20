# Frontend

Minimal React + TypeScript frontend for the Daeva control panel.

## Run

```bash
npm install
npm run dev
```

The dev server starts on `http://127.0.0.1:5173`.

By default it calls the backend at `http://127.0.0.1:8000`.

## Environment Variables

- `VITE_API_BASE_URL`
  Overrides the backend base URL used by the frontend.
  Default: `http://127.0.0.1:8000`

PowerShell example:

```powershell
$env:VITE_API_BASE_URL="http://192.168.1.50:8000"
npm run dev
```
