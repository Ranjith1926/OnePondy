import { newIdempotencyKey } from './idempotencyKey';

describe('newIdempotencyKey', () => {
  it('is different every time, so a new attempt never reuses an old booking', () => {
    const keys = new Set(Array.from({ length: 50 }, () => newIdempotencyKey()));

    expect(keys.size).toBe(50);
  });

  it('fits the API limit of 64 characters', () => {
    expect(newIdempotencyKey().length).toBeLessThanOrEqual(64);
  });

  it('still works where crypto.randomUUID is missing', () => {
    const original = globalThis.crypto;
    Object.defineProperty(globalThis, 'crypto', { value: undefined, configurable: true });
    try {
      expect(newIdempotencyKey()).toMatch(/^[a-z0-9]+-[a-z0-9]+$/);
    } finally {
      Object.defineProperty(globalThis, 'crypto', { value: original, configurable: true });
    }
  });
});
