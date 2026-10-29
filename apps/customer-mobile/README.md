# OnePondy customer app

React Native + Expo (SDK 58) app using development builds and React Navigation. See the repository
[README](../../README.md) for machine setup and the
[functional specification](../../OnePondy_Functional_Specification.md) for product rules.

## Run on the Android emulator

```powershell
# From this folder, with an emulator running (Android Studio > Device Manager)
npm run android   # builds and installs the development build (rebuild after native changes)
npm start         # later runs: start Metro and open the installed OnePondy app
```

Copy `.env.example` to `.env` to point the app at your local API.

Emulator gotchas found while testing payments: if a page inside the app cannot load (Razorpay Checkout shows
`ERR_NAME_NOT_RESOLVED`), start the emulator with `-dns-server 8.8.8.8`. An emulator restored from its quick-boot snapshot
rolls back anything installed since, including a rebuilt development build; start it with `-no-snapshot` after installing, or
reinstall the APK (`android/app/build/outputs/apk/debug`). Metro and the IIS-hosted API both default to port 8081, so run Metro
on another port (`npx expo start --dev-client --port 8082`) and open the app with
`adb shell am start -a android.intent.action.VIEW -d "exp+onepondy://expo-development-client/?url=http%3A%2F%2F10.0.2.2%3A8082"`.

In development builds, **Profile > Developer > Component gallery** shows every design-system
component and state (loading, empty, error, offline) for visual QA in light and dark mode.

## Scripts

| Script | Purpose |
|---|---|
| `npm run android` | Build and install the development build on an emulator or device |
| `npm start` | Start Metro for the installed development build |
| `npm run lint` | ESLint (Expo config + Prettier + spec rules below) |
| `npm run typecheck` | TypeScript strict type-check |
| `npm test` | Jest with React Native Testing Library |

## Rules enforced by tooling

| Rule | Enforced by |
|---|---|
| The design system never imports from `features/` (spec §5) | ESLint `no-restricted-imports` |
| App state never imports API, storage or navigation code (spec §6) | ESLint `no-restricted-imports` |
| No `any` (spec §15) | ESLint `@typescript-eslint/no-explicit-any` |
| No user-facing text outside `src/i18n/locales/en.json` | ESLint `i18next/no-literal-string` (JSX text and label-type props) |
| Translation keys exist | TypeScript (`src/i18n/i18next.d.ts`) |
| Text and control colours meet WCAG AA in light and dark mode | `src/design-system/tokens/colors.test.ts` |

## Folder conventions (spec §5–§6)

| Folder | Holds | Rules |
|---|---|---|
| `src/app/` | `App.tsx` and `providers/` | Not an Expo Router routes directory |
| `src/navigation/` | Root stack and bottom tabs (static React Navigation config) | Auth navigator joins in Phase 2 |
| `src/design-system/` | `tokens/`, `theme/`, `atoms/`, `molecules/`, `organisms/`, `templates/` | Domain-agnostic; takes all text through props |
| `src/features/<feature>/` | `screens/`, `components/`, `hooks/`, `data/` | Feature organisms live in `components/` |
| `src/state/` | Root reducer, slice reducers, actions, action types, `AppStateContext.tsx` | Pure reducers; separate state and dispatch contexts |
| `src/storage/` | Local persistence (AsyncStorage for non-sensitive preferences) | Never tokens or personal data |
| `src/i18n/` | i18next setup and `locales/` | English source; Tamil and French fall back until reviewed |
| `src/api/` | API client, endpoints, error parsing | Phase 2, with TanStack Query |

Sample places and collections in `src/features/*/data/` exist only in development builds and are
always labelled "Sample" in the UI (spec §4).

Tests sit next to the code they cover as `*.test.ts(x)`.
