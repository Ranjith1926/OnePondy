import { describe, expect, it } from 'vitest';

import { foodAreaLabels } from './labels';

describe('food areas', () => {
  // The API validates delivery areas against this list, and the customer app's area picker offers the same ones:
  // a code the console offered that the API did not know would make saving a restaurant fail.
  it('offers exactly the areas the API accepts', () => {
    expect(Object.keys(foodAreaLabels)).toEqual([
      'puducherry',
      'whiteTown',
      'tamilQuarter',
      'auroville',
      'lawspet',
      'muthialpet',
      'reddiarpalayam',
      'ariyankuppam',
      'villianur',
    ]);
  });

  it('labels every area in plain words', () => {
    for (const label of Object.values(foodAreaLabels)) {
      expect(label.trim()).not.toBe('');
    }
    expect(foodAreaLabels.puducherry).toMatch(/whole city/i);
  });
});
