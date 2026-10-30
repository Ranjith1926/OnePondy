# OnePondy driver app

React Native + Expo (SDK 58) app for OnePondy's delivery drivers (Phase 8, step 8.6): sign in with a mobile number, apply to deliver, see the
review result, and go on and off duty. Delivery offers and tracking come in step 8.7. The product rules are in
[`../../docs/phase-8-plan.md`](../../docs/phase-8-plan.md) and workflow §25.8 of [`../../docs/workflows.md`](../../docs/workflows.md).

Its design system, API client, session handling and test setup are a trimmed copy of the customer app's (`../customer-mobile`), so each app builds on its
own. Moving them into a shared package is a later refactor.

## Run on the Android emulator

```powershell
npm run android   # builds and installs the development build (package com.onepondy.driver)
npm start         # later runs: start Metro for the installed app
```

Copy `.env.example` to `.env` to point the app at your local API (the default, `http://10.0.2.2:5080`, reaches the PC from the emulator). The customer
app's README lists the other emulator tips (Metro port clashes, snapshot boots, physical phones); they apply here too, with the scheme
`exp+onepondy-driver://`.

### Windows: "ninja: error: mkdir(...): No such file or directory"

The first native build can fail in `buildCMakeDebug` because the repo lives in a deep folder and the C++ tool cannot create paths over about 248
characters (this app's folder name makes them longer than the customer app's). Workaround used for the first run, none of it committed (`android/` is
generated):

1. `subst R: C:\OnePondy` and run the build from `R:\apps\driver-mobile`.
2. In the generated `android/app/build.gradle`, inside `android { ... }`, add
   `externalNativeBuild { cmake { buildStagingDirectory = file('R:/cx') } }`.

## Scripts

| Script | Purpose |
|---|---|
| `npm run android` | Build and install the development build on an emulator or device |
| `npm start` | Start Metro for the development build |
| `npm run lint` | ESLint (Expo config, Prettier, no `any`, no text outside `src/i18n/locales/en.json`) |
| `npm run typecheck` | TypeScript strict type-check |
| `npm test` | Jest with React Native Testing Library |

## What it does not do yet

Upload photos of licence or registration, receive delivery offers, collect location (step 8.7 will, during a delivery only), send or receive push
notifications, show earnings, or offer Tamil and French.
