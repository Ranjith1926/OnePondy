import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError, publicApi } from '@/lib/api';
import type { SharedRide } from '@/lib/apiTypes';

import SharedTripPage from './page';

vi.mock('@/lib/api', () => {
  class ApiError extends Error {
    status: number;
    code: string;

    constructor(status: number, code: string, message: string) {
      super(message);
      this.status = status;
      this.code = code;
    }
  }
  return { ApiError, publicApi: vi.fn() };
});

const read = vi.mocked(publicApi);

function trip(overrides: Partial<SharedRide> = {}): SharedRide {
  return {
    ended: false,
    status: 'accepted',
    riderFirstName: 'Anita',
    driverFirstName: 'Ravi',
    vehicleClass: 'auto',
    vehicleNumber: 'PY01AB1234',
    pickupLabel: 'Promenade Beach',
    destinationLabel: 'Auroville',
    driverLocation: { latitude: 11.95, longitude: 79.83, at: '2026-11-03T08:00:00Z' },
    updatedAt: '2026-11-03T08:00:00Z',
    ...overrides,
  };
}

const params = (token: string) => ({ params: Promise.resolve({ token }) });

beforeEach(() => {
  read.mockReset();
});

describe('<SharedTripPage />', () => {
  it('shows who is driving, the vehicle, where the trip goes and a map link to the driver', async () => {
    read.mockResolvedValue(trip());

    render(await SharedTripPage(params('a-secret')));

    expect(read).toHaveBeenCalledWith('/api/v1/shared-rides/a-secret');
    expect(screen.getByText("Anita's trip")).toBeInTheDocument();
    expect(screen.getByText('The driver is on the way to the pickup.')).toBeInTheDocument();
    expect(screen.getByText('Promenade Beach')).toBeInTheDocument();
    expect(screen.getByText(/Ravi · Auto · PY01AB1234/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open on the map' })).toHaveAttribute(
      'href',
      expect.stringContaining('mlat=11.95&mlon=79.83'),
    );
  });

  it('shows only that the trip ended once it has', async () => {
    read.mockResolvedValue(
      trip({
        ended: true,
        riderFirstName: null,
        driverFirstName: null,
        vehicleClass: null,
        vehicleNumber: null,
        pickupLabel: null,
        destinationLabel: null,
        driverLocation: null,
      }),
    );

    render(await SharedTripPage(params('a-secret')));

    expect(screen.getByText('This trip has ended.')).toBeInTheDocument();
    expect(screen.queryByText('Ravi', { exact: false })).not.toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('says the link is unavailable for an unknown, turned-off or old link, without saying which', async () => {
    read.mockImplementation(async () => {
      throw new ApiError(404, 'http_404', 'Not found');
    });

    render(await SharedTripPage(params('made-up')));

    expect(screen.getByText(/This link is no longer available/)).toBeInTheDocument();
  });

  it('does not hide a real failure behind the same message', async () => {
    read.mockImplementation(async () => {
      throw new ApiError(500, 'http_500', 'Broken');
    });

    await expect(SharedTripPage(params('a-secret'))).rejects.toThrow('Broken');
  });
});
