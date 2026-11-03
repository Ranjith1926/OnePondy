import { aRide, withDriver } from '../../test-utils/rideFixture';
import {
  canCancel,
  canReport,
  hasDriver,
  isOpen,
  landmarks,
  refetchInterval,
  rupees,
} from './rideLogic';
import { rideErrorMessageKey } from './rideErrors';
import { ApiError, NetworkError } from '../../api/apiErrors';

describe('ride status helpers', () => {
  it('knows which rides are still going', () => {
    expect(
      ['requested', 'accepted', 'arrived', 'inProgress'].every((s) => isOpen(s as never)),
    ).toBe(true);
    expect(['completed', 'cancelled', 'expired'].some((s) => isOpen(s as never))).toBe(false);
  });

  it('lets the rider cancel only until the trip starts', () => {
    expect(canCancel('requested')).toBe(true);
    expect(canCancel('accepted')).toBe(true);
    expect(canCancel('arrived')).toBe(true);
    expect(canCancel('inProgress')).toBe(false);
    expect(canCancel('completed')).toBe(false);
  });

  it('offers emergency and sharing only while a driver is with the rider', () => {
    expect(hasDriver('requested')).toBe(false);
    expect(hasDriver('accepted')).toBe(true);
    expect(hasDriver('inProgress')).toBe(true);
    expect(hasDriver('completed')).toBe(false);
  });

  it('lets a rider report only once a driver had taken the ride', () => {
    expect(canReport(aRide())).toBe(false);
    expect(canReport(withDriver())).toBe(true);
  });
});

describe('refetchInterval', () => {
  it('polls quickly while the live connection is down and slowly while it is up', () => {
    expect(refetchInterval(withDriver(), false)).toBe(10_000);
    expect(refetchInterval(withDriver(), true)).toBe(60_000);
  });

  it('stops once the ride has ended, or when there is no ride', () => {
    expect(refetchInterval(aRide({ status: 'completed' }), false)).toBe(false);
    expect(refetchInterval(null, false)).toBe(false);
    expect(refetchInterval(undefined, true)).toBe(false);
  });
});

describe('rupees', () => {
  it('shows whole rupees plainly and others with paise', () => {
    expect(rupees(203)).toBe('₹203');
    expect(rupees(203.5)).toBe('₹203.50');
  });
});

describe('landmarks', () => {
  it('are on Earth and distinct', () => {
    expect(new Set(landmarks.map((l) => l.id)).size).toBe(landmarks.length);
    for (const place of landmarks) {
      expect(Math.abs(place.latitude)).toBeLessThanOrEqual(90);
      expect(Math.abs(place.longitude)).toBeLessThanOrEqual(180);
    }
  });
});

describe('rideErrorMessageKey', () => {
  it('maps what the server says to a message, and falls back kindly', () => {
    expect(rideErrorMessageKey(new ApiError(409, 'ride_no_drivers'))).toBe(
      'rides.errors.ride_no_drivers',
    );
    expect(rideErrorMessageKey(new ApiError(500, 'something_new'))).toBe('rides.errors.generic');
    expect(rideErrorMessageKey(new NetworkError('offline'))).toBe('rides.errors.offline');
    expect(rideErrorMessageKey(new NetworkError('timeout'))).toBe('rides.errors.timeout');
    expect(rideErrorMessageKey(new Error('x'))).toBe('rides.errors.generic');
  });
});
