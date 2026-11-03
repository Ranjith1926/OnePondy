import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { publicApi } from '@/lib/api';
import type { GrievanceOfficer } from '@/lib/apiTypes';

import GrievancePage from './page';

vi.mock('@/lib/api', () => ({ publicApi: vi.fn() }));

const read = vi.mocked(publicApi);

function officer(overrides: Partial<GrievanceOfficer> = {}): GrievanceOfficer {
  return {
    configured: false,
    name: null,
    email: null,
    phone: null,
    address: null,
    acknowledgeWithinHours: 48,
    resolveWithinDays: 30,
    ...overrides,
  };
}

beforeEach(() => read.mockReset());

describe('<GrievancePage />', () => {
  it('names the grievance officer and how to reach them, and what to expect', async () => {
    read.mockResolvedValue(
      officer({
        configured: true,
        name: 'R. Kumar',
        email: 'grievance@onepondy.example',
        phone: '+91 413 000 0000',
        address: '1 Rue Suffren\nPuducherry',
      }),
    );

    render(await GrievancePage());

    expect(screen.getByText('R. Kumar')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'grievance@onepondy.example' })).toHaveAttribute(
      'href',
      'mailto:grievance@onepondy.example',
    );
    expect(screen.getByText('+91 413 000 0000', { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/within 48 hours/)).toBeInTheDocument();
    expect(screen.getByText(/within\s+30 days/)).toBeInTheDocument();
    expect(read).toHaveBeenCalledWith('/api/v1/support/grievance');
  });

  it('says plainly that no one has been appointed yet, instead of showing a blank', async () => {
    read.mockResolvedValue(officer());

    render(await GrievancePage());

    expect(screen.getByText('A grievance officer has not been appointed yet.')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});
