import { formatDateTime } from './formatDateTime';

describe('formatDateTime', () => {
  it('shows the day and time without seconds', () => {
    const text = formatDateTime('2026-10-30T03:30:00.000Z');

    expect(text).toMatch(/30/);
    expect(text).toMatch(/Oct/);
    expect(text).not.toMatch(/:\d\d:\d\d/);
  });
});
