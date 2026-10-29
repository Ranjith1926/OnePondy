import { formatCountdown, formatMoney } from './formatMoney';

describe('formatMoney', () => {
  it('shows rupees without empty paise', () => {
    expect(formatMoney(1000, 'INR')).toMatch(/₹\s?1,000$/);
  });

  it('shows paise when there are some', () => {
    expect(formatMoney(249.5, 'INR')).toMatch(/249\.50$/);
  });

  it('falls back to the plain currency code when the currency is not valid', () => {
    expect(formatMoney(10, 'not a currency')).toBe('not a currency 10');
  });
});

describe('formatCountdown', () => {
  it.each([
    [1800, '30:00'],
    [90, '1:30'],
    [5, '0:05'],
    [0, '0:00'],
    [-12, '0:00'],
    [59.9, '0:59'],
  ])('shows %p seconds as %s', (seconds, expected) => {
    expect(formatCountdown(seconds)).toBe(expected);
  });
});
