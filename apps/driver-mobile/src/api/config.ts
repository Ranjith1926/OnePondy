// One base URL per environment, set through EXPO_PUBLIC_API_BASE_URL (spec §8). The Android
// emulator reaches the development PC at 10.0.2.2; see .env.example for physical devices.
// A trailing slash is dropped because request paths always start with one.
export const apiBaseUrl = (process.env.EXPO_PUBLIC_API_BASE_URL ?? 'http://10.0.2.2:5080').replace(
  /\/+$/,
  '',
);

export const requestTimeoutMs = 15_000;
