import { beforeEach, describe, expect, it, vi } from 'vitest';

import { api } from '@/lib/api';
import { requireSupport } from '@/lib/session';

import { changeSafetyReport } from './actions';

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

vi.mock('@/lib/session', () => ({ requireSupport: vi.fn() }));

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
const reportId = 'd0000000-0000-4000-8000-000000000009';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('id', reportId);
  data.set('version', '77');
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

async function redirectedTo(run: () => Promise<unknown>): Promise<string> {
  try {
    await run();
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
  vi.mocked(requireSupport).mockReset();
});

describe('changeSafetyReport', () => {
  const run = (fields: Record<string, string>) =>
    changeSafetyReport({ status: 'idle' }, form(fields));

  it('checks the person may work support before anything else', async () => {
    vi.mocked(requireSupport).mockRejectedValue(new Error('NEXT_REDIRECT /?denied=1'));

    await expect(run({ step: 'acknowledge' })).rejects.toThrow('NEXT_REDIRECT /?denied=1');
    expect(call).not.toHaveBeenCalled();
  });

  it('takes a report with the version the person was looking at', async () => {
    expect(await redirectedTo(() => run({ step: 'acknowledge' }))).toBe(
      `/safety/${reportId}?done=acknowledge`,
    );

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/ride-safety/${reportId}/acknowledge`, {
      method: 'POST',
      json: { version: 77 },
    });
  });

  it('closes a report only with a note, and sends the note', async () => {
    const empty = await run({ step: 'resolve', note: '  ' });
    expect(empty.fieldErrors?.note).toBeDefined();
    expect(call).not.toHaveBeenCalled();

    expect(await redirectedTo(() => run({ step: 'resolve', note: 'Called both; safe.' }))).toBe(
      `/safety/${reportId}?done=resolve`,
    );
    expect(call).toHaveBeenCalledWith(`/api/v1/admin/ride-safety/${reportId}/resolve`, {
      method: 'POST',
      json: { version: 77, note: 'Called both; safe.' },
    });
  });

  it('refuses a note that is too long', async () => {
    const result = await run({ step: 'resolve', note: 'x'.repeat(1001) });

    expect(result.fieldErrors?.note).toBeDefined();
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses an unknown step, an id that is not a GUID and a missing version, before calling the API', async () => {
    expect((await run({ step: 'delete' })).status).toBe('error');
    expect(
      (
        await changeSafetyReport(
          { status: 'idle' },
          (() => {
            const data = form({ step: 'acknowledge' });
            data.set('id', '../../admin');
            return data;
          })(),
        )
      ).status,
    ).toBe('error');
    expect(
      (
        await changeSafetyReport(
          { status: 'idle' },
          (() => {
            const data = form({ step: 'acknowledge' });
            data.set('version', '');
            return data;
          })(),
        )
      ).status,
    ).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });
});
