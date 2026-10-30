import { beforeEach, describe, expect, it, vi } from 'vitest';

import { partnerApi } from '@/lib/api';

import { changeOrderStatus } from './actions';

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

const api = vi.mocked(partnerApi);
const businessId = 'b0000000-0000-4000-8000-000000000001';
const orderId = 'o0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('businessId', businessId);
  data.set('id', orderId);
  data.set('version', '4242');
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

const run = (fields: Record<string, string>) => changeOrderStatus({ status: 'idle' }, form(fields));

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
  api.mockReset();
  api.mockResolvedValue(undefined);
});

describe('changeOrderStatus', () => {
  it('accepts with the version the restaurant was looking at', async () => {
    await redirectedTo({ step: 'accept' });

    expect(api).toHaveBeenCalledWith(
      `/api/v1/partner/businesses/${businessId}/orders/${orderId}/accept`,
      {
        method: 'POST',
        json: { version: 4242 },
      },
    );
  });

  it.each([
    ['preparing', 'preparing'],
    ['ready', 'ready'],
  ])('moves the order on with %s', async (step, path) => {
    await redirectedTo({ step });

    expect(api).toHaveBeenCalledWith(
      `/api/v1/partner/businesses/${businessId}/orders/${orderId}/${path}`,
      { method: 'POST', json: { version: 4242 } },
    );
  });

  it('sends the reason with a rejection or a cancellation', async () => {
    await redirectedTo({ step: 'reject', reason: '  Out of batter  ' });
    await redirectedTo({ step: 'cancel', reason: 'Gas ran out' });

    expect(api).toHaveBeenNthCalledWith(1, expect.stringMatching(/\/reject$/), {
      method: 'POST',
      json: { version: 4242, reason: 'Out of batter' },
    });
    expect(api).toHaveBeenNthCalledWith(2, expect.stringMatching(/\/cancel$/), {
      method: 'POST',
      json: { version: 4242, reason: 'Gas ran out' },
    });
  });

  it('does not reject or cancel without telling the customer why, and does not call the API', async () => {
    for (const step of ['reject', 'cancel']) {
      const state = await run({ step, reason: '   ' });

      expect(state.status).toBe('error');
      expect(state.fieldErrors?.reason?.[0]).toMatch(/tell the customer/i);
    }
    expect(api).not.toHaveBeenCalled();
  });

  it('refuses to act without a real version rather than sending a made-up one', async () => {
    for (const version of ['', 'abc', '-1', '1.5']) {
      const state = await run({ step: 'accept', version });

      expect(state.status).toBe('error');
      expect(state.message).toMatch(/out of date/i);
    }
    expect(api).not.toHaveBeenCalled();
  });

  it('refuses an action it does not know', async () => {
    const state = await run({ step: 'deliver' });

    expect(state).toEqual({ status: 'error', message: 'Unknown action.' });
    expect(api).not.toHaveBeenCalled();
  });

  it('tells the restaurant when the order changed since they looked, and does not redirect', async () => {
    const { ApiError } = await import('@/lib/api');
    api.mockRejectedValue(
      new ApiError(
        409,
        'order_changed',
        'This order changed since you loaded it. Reload and try again.',
      ),
    );

    const state = await run({ step: 'accept' });

    expect(state).toMatchObject({
      status: 'error',
      message: expect.stringMatching(/changed since you loaded it/),
    });
  });

  it('goes back to the same view of the inbox with a note of what was done', async () => {
    expect(await redirectedTo({ step: 'accept', view: 'preparing' })).toBe(
      `/partner/businesses/${businessId}/orders?done=accept&view=preparing`,
    );
    expect(await redirectedTo({ step: 'ready' })).toBe(
      `/partner/businesses/${businessId}/orders?done=ready`,
    );
  });
});
