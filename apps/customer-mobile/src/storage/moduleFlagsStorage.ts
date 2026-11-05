import AsyncStorage from '@react-native-async-storage/async-storage';

// The last module flags the server sent, so a slow or missing answer on the next launch does not turn working modules back into "coming soon".
// Non-sensitive; the server's next answer always replaces it.
const STORAGE_KEY = 'onepondy.module-flags.v1';

/** Keeps only string-valued flags, so corrupted or outdated data is ignored. */
export function parseStoredModuleFlags(value: unknown): Record<string, string> | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }
  const flags: Record<string, string> = {};
  for (const [key, flag] of Object.entries(value)) {
    if (typeof flag === 'string') {
      flags[key] = flag;
    }
  }
  return Object.keys(flags).length > 0 ? flags : undefined;
}

export async function loadStoredModuleFlags(): Promise<Record<string, string> | undefined> {
  const raw = await AsyncStorage.getItem(STORAGE_KEY);
  if (raw === null) {
    return undefined;
  }
  try {
    return parseStoredModuleFlags(JSON.parse(raw));
  } catch {
    return undefined;
  }
}

export async function saveStoredModuleFlags(flags: Record<string, string>): Promise<void> {
  await AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(flags));
}
