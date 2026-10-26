import { isBelowMinimum } from './appVersion';
import { effectiveModuleStatus } from './moduleConfig';

describe('effectiveModuleStatus', () => {
  it('is "coming soon" for everything until the server answers', () => {
    expect(Object.values(effectiveModuleStatus(undefined))).toEqual(Array(7).fill('comingSoon'));
  });

  it('follows the server flags for hidden and coming soon', () => {
    const status = effectiveModuleStatus({ food: 'hidden', rides: 'comingSoon' });

    expect(status.food).toBe('hidden');
    expect(status.rides).toBe('comingSoon');
    expect(status.stays).toBe('comingSoon');
  });

  it('never shows a module as available when this app version cannot open it', () => {
    expect(effectiveModuleStatus({ food: 'available' }).food).toBe('comingSoon');
  });

  it('ignores unknown values', () => {
    expect(effectiveModuleStatus({ food: 'live', casino: 'available' }).food).toBe('comingSoon');
  });
});

describe('isBelowMinimum', () => {
  it.each([
    ['0.1.0', '0.1.0', false],
    ['0.1.0', '0.2.0', true],
    ['0.10.0', '0.9.9', false],
    ['1.0.0', '0.99.99', false],
    ['0.1.0', 'latest', false],
    [undefined, '9.0.0', false],
  ])('app %s with minimum %s needs an update: %s', (app, minimum, expected) => {
    expect(isBelowMinimum(app, minimum)).toBe(expected);
  });
});
