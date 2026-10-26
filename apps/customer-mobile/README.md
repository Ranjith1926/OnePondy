# OnePondy customer app

React Native + Expo (SDK 58) app using development builds and React Navigation. See the repository
[README](../../README.md) for machine setup and the
[functional specification](../../OnePondy_Functional_Specification.md) for product rules.

## Run on the Android emulator

```powershell
# From this folder, with an emulator running (Android Studio > Device Manager)
npm run android   # first run builds and installs the development build (several minutes)
npm start         # later runs: start Metro and open the installed OnePondy app
```

Copy `.env.example` to `.env` to point the app at your local API.

## Scripts

| Script | Purpose |
|---|---|
| `npm run android` | Build and install the development build on an emulator or device |
| `npm start` | Start Metro for the installed development build |
| `npm run lint` | ESLint (Expo config + Prettier + spec boundary rules) |
| `npm run typecheck` | TypeScript strict type-check |
| `npm test` | Jest with React Native Testing Library |

## Folder conventions (spec §5–§6)

Folders are created when the phase that needs them starts.

| Folder | Holds | Rules |
|---|---|---|
| `src/app/` | `App.tsx` and `providers/` | Not an Expo Router routes directory |
| `src/navigation/` | Root, auth and tab navigators | Phase 1 |
| `src/design-system/` | `tokens/`, `atoms/`, `molecules/`, `organisms/`, `templates/` | Domain-agnostic; must not import `features/` (lint-enforced) |
| `src/features/<feature>/` | `screens/`, `components/`, `hooks/`, `services/`, `schemas/`, `types/` | Feature organisms live in `components/` |
| `src/state/` | Root reducer, actions, action types, `AppStateContext.tsx` | Pure; no API, storage or navigation imports (lint-enforced) |
| `src/i18n/` | i18next setup and `locales/` | All user-facing strings |
| `src/api/` | API client, endpoints, error parsing | Server state goes through TanStack Query |

Tests sit next to the code they cover as `*.test.ts(x)`.
