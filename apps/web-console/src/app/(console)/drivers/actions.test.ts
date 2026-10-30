import { beforeEach, describe, expect, it, vi } from 'vitest';

import { api, ApiError } from '@/lib/api';

import { changeDriverStatus } from './actions';

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
  data.set('id', driverId);
  data.set('version', '4242');
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

const run = (fields: Record<string, string>) =>
  changeDriverStatus({ status: 'idle' }, form(fields));

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

describe('changeDriverStatus', () => {
  it('approves with the version the reviewer was looking at', async () => {
    const to = await redirectedTo({ step: 'approve' });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/drivers/${driverId}/approve`, {
      method: 'POST',
      json: { version: 4242 },
    });
    expect(to).toBe(`/drivers/${driverId}?done=approve`);
  });

  it('reinstates without a reason', async () => {
    await redirectedTo({ step: 'reinstate', reason: 'ignored' });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/drivers/${driverId}/reinstate`, {
      method: 'POST',
      json: { version: 4242 },
    });
  });

  it('sends a trimmed reason with a rejection or a suspension', async () => {
    await redirectedTo({ step: 'reject', reason: '  Licence unreadable  ' });
    await redirectedTo({ step: 'suspend', reason: 'Complaint' });

    expect(call).toHaveBeenNthCalledWith(1, `/api/v1/admin/drivers/${driverId}/reject`, {
      method: 'POST',
      json: { version: 4242, reason: 'Licence unreadable' },
    });
    expect(call).toHaveBeenNthCalledWith(2, `/api/v1/admin/drivers/${driverId}/suspend`, {
      method: 'POST',
      json: { version: 4242, reason: 'Complaint' },
    });
  });

  it('asks for a reason before rejecting or suspending, and sends nothing', async () => {
    const rejected = await run({ step: 'reject', reason: '   ' });
    const suspended = await run({ step: 'suspend' });

    expect(rejected.fieldErrors?.reason).toBeDefined();
    expect(suspended.fieldErrors?.reason).toBeDefined();
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses unknown steps, ids that are not GUIDs and a missing version', async () => {
    expect((await run({ step: 'delete' })).status).toBe('error');
    expect((await run({ step: 'approve', id: '../../admin/staff' })).status).toBe('error');
    expect((await run({ step: 'approve', version: '' })).status).toBe('error');
    expect((await run({ step: 'approve', version: '0' })).status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the API message when the driver changed under the reviewer', async () => {
    call.mockRejectedValue(
      new ApiError(409, 'driver_changed', 'This changed since you loaded it.'),
    );

    const state = await run({ step: 'approve' });

    expect(state.status).toBe('error');
    expect(state.message).toContain('changed');
  });
});
