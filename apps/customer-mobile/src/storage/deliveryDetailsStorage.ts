import * as SecureStore from 'expo-secure-store';

// The last delivery address and phone, so the next order does not need them typed again. They are personal details, so they live in
// SecureStore beside the session (spec §6), belong to one signed-in user, and are cleared when that user signs out.
const STORAGE_KEY = 'onepondy.delivery-details.v1';

export interface DeliveryDetails {
  address: string;
  phone: string;
  /** The delivery area chosen for that order, if any. */
  area?: string;
}

interface StoredDeliveryDetails extends DeliveryDetails {
  userId: string;
}

function parse(value: unknown): StoredDeliveryDetails | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const record = value as Record<string, unknown>;
  if (
    typeof record.userId !== 'string' ||
    typeof record.address !== 'string' ||
    typeof record.phone !== 'string'
  ) {
    return null;
  }
  return {
    userId: record.userId,
    address: record.address,
    phone: record.phone,
    area: typeof record.area === 'string' ? record.area : undefined,
  };
}

/** The saved details, only if they were saved by this same user: someone else signing in on this phone never sees them. */
export async function loadDeliveryDetails(userId: string): Promise<DeliveryDetails | null> {
  const raw = await SecureStore.getItemAsync(STORAGE_KEY);
  if (!raw) {
    return null;
  }
  try {
    const stored = parse(JSON.parse(raw));
    return stored !== null && stored.userId === userId
      ? { address: stored.address, phone: stored.phone, area: stored.area }
      : null;
  } catch {
    return null;
  }
}

export async function saveDeliveryDetails(userId: string, details: DeliveryDetails): Promise<void> {
  const stored: StoredDeliveryDetails = { userId, ...details };
  await SecureStore.setItemAsync(STORAGE_KEY, JSON.stringify(stored));
}

export async function clearDeliveryDetails(): Promise<void> {
  await SecureStore.deleteItemAsync(STORAGE_KEY);
}
