import { beforeEach, describe, expect, it, vi } from 'vitest';

import { partnerApi } from '@/lib/api';

import { changeBookingStatus } from './actions';

vi.mock('@/lib/api', () => {
  class ApiError extends Error {
    constructor(
      readonly status: number,
      readonly code: string,
      message: string,
      readonly fieldErrors: Record<string, string[]> = {},
    ) {
      super(message);
    }
  }
  return { ApiError, partnerApi: vi.fn() };
});

vi.mock('next/navigation', () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT ${url}`);
  },
  unstable_rethrow: (error: unknown) => {
    if (error instanceof Error && error.message.startsWith('NEXT_REDIRECT')) {
      throw error;
    }
  },
}));

const api = vi.mocked(partnerApi);
const businessId = 'b0000000-0000-4000-8000-000000000001';
const bookingId = 'k0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('businessId', businessId);
  data.set('id', bookingId);
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

beforeEach(() => {
  api.mockReset();
  api.mockResolvedValue(undefined);
});

// The API refuses any action whose version is not the booking's current one, so sending 0 (the form once sent none) could never work.
describe('changeBookingStatus', () => {
  it('sends the version of the booking the partner was looking at', async () => {
    await expect(
      changeBookingStatus({ status: 'idle' }, form({ step: 'accept', version: '777' })),
    ).rejects.toThrow(/^NEXT_REDIRECT/);

    expect(api).toHaveBeenCalledWith(
      `/api/v1/partner/businesses/${businessId}/bookings/${bookingId}/accept`,
      {
        method: 'POST',
        json: { version: 777, reason: null },
      },
    );
  });

  it('does not act when the form carries no version', async () => {
    const state = await changeBookingStatus({ status: 'idle' }, form({ step: 'accept' }));

    expect(state.status).toBe('error');
    expect(state.message).toMatch(/out of date/i);
    expect(api).not.toHaveBeenCalled();
  });

  it('still needs a reason to reject', async () => {
    const state = await changeBookingStatus(
      { status: 'idle' },
      form({ step: 'reject', version: '777' }),
    );

    expect(state.fieldErrors?.reason).toBeDefined();
    expect(api).not.toHaveBeenCalled();
  });
});
