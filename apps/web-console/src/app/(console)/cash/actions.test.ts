import { beforeEach, describe, expect, it, vi } from 'vitest';

import { api, ApiError } from '@/lib/api';

import { recordDeposit } from './actions';

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
  return { ApiError, api: vi.fn() };
});

vi.mock('@/lib/session', () => ({ requireAdministrator: vi.fn() }));

// A Next.js redirect is an exception the framework catches; here it is one the test can read.
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

const call = vi.mocked(api);
const driverId = 'd0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('driverUserId', driverId);
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

const run = (fields: Record<string, string>) => recordDeposit({ status: 'idle' }, form(fields));

async function redirectedTo(fields: Record<string, string>): Promise<string> {
  try {
    await run(fields);
  } catch (error) {
    const message = (error as Error).message;
    if (message.startsWith('NEXT_REDIRECT ')) {
      return message.slice('NEXT_REDIRECT '.length);
    }
    throw error;
  }
  throw new Error('expected the action to redirect');
}

beforeEach(() => {
  call.mockReset();
  call.mockResolvedValue(undefined);
});

describe('recordDeposit', () => {
  it('sends the amount as a number with a trimmed reference and note, then shows the list', async () => {
    const to = await redirectedTo({ amount: ' 139.25 ', reference: '  REC-1 ', note: ' part ' });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/cash/drivers/${driverId}/deposits`, {
      method: 'POST',
      json: { amount: 139.25, reference: 'REC-1', note: 'part' },
    });
    expect(to).toBe('/cash?recorded=1');
  });

  it('leaves out a reference or note that was not given', async () => {
    await redirectedTo({ amount: '200', reference: '   ' });

    expect(call).toHaveBeenCalledWith(expect.any(String), {
      method: 'POST',
      json: { amount: 200, reference: null, note: null },
    });
  });

  it.each(['', '0', '-5', '12.345', 'abc', '1e3', '12,50', '100000000'])(
    'refuses the amount "%s" before asking the server',
    async (amount) => {
      const state = await run({ amount });

      expect(state.fieldErrors?.amount).toBeDefined();
      expect(call).not.toHaveBeenCalled();
    },
  );

  it('refuses an id that is not a GUID, so nothing odd goes into a request path', async () => {
    const state = await run({ driverUserId: '../../admin/staff', amount: '10' });

    expect(state.status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the server message when the driver holds less than that', async () => {
    call.mockRejectedValue(
      new ApiError(400, 'validation_failed', 'Check the highlighted fields.', {
        amount: ['This driver holds only \u20b9100.00.'],
      }),
    );

    const state = await run({ amount: '150' });

    expect(state.status).toBe('error');
    expect(state.fieldErrors?.amount?.[0]).toContain('holds only');
  });
});
