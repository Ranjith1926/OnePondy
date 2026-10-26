This is the OnePondy customer app (Expo + React Native). The product and architecture rules live in
`../../OnePondy_Functional_Specification.md`; read it before making changes, and implement only the
current phase (spec §15).

## Expo changes between SDKs — check the versioned docs

Expo ships breaking changes every SDK release. Before writing code that touches an Expo, EAS or
React Native API:

1. Read the major version of the `expo` package in `package.json`.
2. Use the matching versioned docs: `https://docs.expo.dev/versions/v<major>.0.0/`.
3. For anything else, start from https://docs.expo.dev/llms.txt.

## Commands

```bash
npx expo install <package>   # use instead of npm install: resolves SDK-compatible versions
npm run android              # build and install the development build (expo run:android)
npm start                    # start Metro for the development build
npm run lint                 # ESLint + Prettier
npm run typecheck            # tsc --noEmit
npm test                     # Jest (jest-expo) + React Native Testing Library
npx expo-doctor              # diagnose dependency and config issues
```

Run lint, typecheck and tests before declaring any task done.

## Navigation

- Use **React Navigation** (spec §3). Do **not** install Expo Router: `src/app/` holds the app entry
  point and providers, not route files.

## Project rules

- Folder layout, Atomic Design and state boundaries: spec §5 and §6, summarised in `README.md`.
- The design system never imports from `features/`; app state never calls APIs, storage or
  navigation. ESLint enforces both.
- If `ios/` and `android/` do not exist they are generated (Continuous Native Generation). Never
  edit them by hand — configure native behaviour in `app.json` and config plugins.
- After adding a library with native code, rebuild the development build (`npm run android`).
- Never fake payment, booking, ride, delivery or provider availability (spec §15).
