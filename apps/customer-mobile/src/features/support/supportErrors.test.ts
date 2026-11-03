import { ApiError, NetworkError } from '../../api/apiErrors';
import { supportErrorMessageKey } from './supportErrors';

describe('supportErrorMessageKey', () => {
  it.each([
    'support_unavailable',
    'support_too_many_open',
    'support_too_many_messages',
    'support_cannot_reopen',
    'support_linked_not_found',
    'ticket_not_found',
    'support_changed',
    'validation_failed',
  ])('has its own message for %s', (code) => {
    expect(supportErrorMessageKey(new ApiError(409, code))).toBe(`support.errors.${code}`);
  });

  it('tells a person who is offline, or timed out, so', () => {
    expect(supportErrorMessageKey(new NetworkError('offline'))).toBe('support.errors.offline');
    expect(supportErrorMessageKey(new NetworkError('timeout'))).toBe('support.errors.timeout');
  });

  it('falls back to a general message for anything it does not know', () => {
    expect(supportErrorMessageKey(new ApiError(500, 'http_500'))).toBe('support.errors.generic');
    expect(supportErrorMessageKey(new Error('boom'))).toBe('support.errors.generic');
  });
});
