import { beforeEach, describe, expect, it, vi } from 'vitest';

import { api, ApiError } from '@/lib/api';

import { changeOrder } from './actions';

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
const orderId = 'a0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('id', orderId);
  data.set('version', '4242');
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

const run = (fields: Record<string, string>) => changeOrder({ status: 'idle' }, form(fields));

/** Runs the action once and returns where it redirected to (a successful action always redirects). */
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

describe('changeOrder', () => {
  it('cancels with the order version and a trimmed reason', async () => {
    const to = await redirectedTo({ step: 'cancel', reason: '  Driver unreachable ' });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/food-orders/${orderId}/cancel`, {
      method: 'POST',
      json: { version: 4242, reason: 'Driver unreachable' },
    });
    expect(to).toBe(`/orders/${orderId}?done=cancel`);
  });

  it('offers the delivery to other drivers with the delivery version and no reason', async () => {
    const to = await redirectedTo({ step: 'release', reason: 'ignored' });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/food-orders/${orderId}/release-delivery`, {
      method: 'POST',
      json: { version: 4242 },
    });
    expect(to).toBe(`/orders/${orderId}?done=release`);
  });

  it('asks for a reason before cancelling, and sends nothing', async () => {
    const blank = await run({ step: 'cancel', reason: '   ' });
    const missing = await run({ step: 'cancel' });

    expect(blank.fieldErrors?.reason).toBeDefined();
    expect(missing.fieldErrors?.reason).toBeDefined();
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses unknown steps, ids that are not GUIDs and a missing or made-up version', async () => {
    expect((await run({ step: 'delete' })).status).toBe('error');
    expect((await run({ step: 'cancel', id: '../../admin/staff', reason: 'x' })).status).toBe(
      'error',
    );
    expect((await run({ step: 'release', version: '' })).status).toBe('error');
    expect((await run({ step: 'release', version: '0' })).status).toBe('error');
    expect((await run({ step: 'release', version: '1.5' })).status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the API message when the order or delivery changed under the administrator', async () => {
    call.mockRejectedValue(new ApiError(409, 'delivery_changed', 'This delivery changed.'));

    const state = await run({ step: 'release' });

    expect(state.status).toBe('error');
    expect(state.message).toContain('changed');
  });
});
