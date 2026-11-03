import { describe, expect, it } from 'vitest';

import { isSafetyStep, mapLink, parseSafetyView } from './safety';

describe('parseSafetyView', () => {
  it('knows the lists and falls back to the waiting ones', () => {
    expect(parseSafetyView('resolved')).toBe('resolved');
    expect(parseSafetyView('all')).toBe('all');
    expect(parseSafetyView('nonsense')).toBe('open');
    expect(parseSafetyView(undefined)).toBe('open');
  });
});

describe('isSafetyStep', () => {
  it('accepts only the two steps', () => {
    expect(isSafetyStep('acknowledge')).toBe(true);
    expect(isSafetyStep('resolve')).toBe(true);
    expect(isSafetyStep('delete')).toBe(false);
    expect(isSafetyStep(null)).toBe(false);
  });
});

describe('mapLink', () => {
  it('links to the place only when there is a real position', () => {
    expect(mapLink(11.95, 79.83)).toBe(
      'https://www.openstreetmap.org/?mlat=11.95&mlon=79.83#map=17/11.95/79.83',
    );
    expect(mapLink(null, 79.83)).toBeNull();
    expect(mapLink(11.95, null)).toBeNull();
    expect(mapLink(Number.NaN, 79.83)).toBeNull();
  });
});
