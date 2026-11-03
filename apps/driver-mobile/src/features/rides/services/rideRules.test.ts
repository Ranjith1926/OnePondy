import { ApiError, NetworkError } from '../../../api/apiErrors';
import { rideErrorMessageKey } from './errorMessages';
import {
  canEndWithReason,
  canGiveUp,
  isValidInsuranceDate,
  nextStep,
  rupees,
  tidyNumber,
} from './rideRules';

describe('isValidInsuranceDate', () => {
  const today = new Date(2026, 10, 3);

  it('accepts a real date that has not passed, including today', () => {
    expect(isValidInsuranceDate('2027-03-31', today)).toBe(true);
    expect(isValidInsuranceDate('2026-11-03', today)).toBe(true);
    expect(isValidInsuranceDate(' 2026-11-04 ', today)).toBe(true);
  });

  it('refuses yesterday, dates that do not exist, and anything not in the form', () => {
    expect(isValidInsuranceDate('2026-11-02', today)).toBe(false);
    expect(isValidInsuranceDate('2027-02-30', today)).toBe(false);
    expect(isValidInsuranceDate('2027-13-01', today)).toBe(false);
    expect(isValidInsuranceDate('31/03/2027', today)).toBe(false);
    expect(isValidInsuranceDate('', today)).toBe(false);
  });
});

describe('ride steps', () => {
  it('moves accepted to arrived to started to complete, and nothing after', () => {
    expect(nextStep('accepted')).toBe('arrive');
    expect(nextStep('arrived')).toBe('start');
    expect(nextStep('inProgress')).toBe('complete');
    expect(nextStep('completed')).toBeNull();
    expect(nextStep('cancelled')).toBeNull();
  });

  it('lets a driver give a ride up only before arriving and end it only once arrived', () => {
    expect(canGiveUp('accepted')).toBe(true);
    expect(canGiveUp('arrived')).toBe(false);
    expect(canEndWithReason('arrived')).toBe(true);
    expect(canEndWithReason('accepted')).toBe(false);
    expect(canEndWithReason('inProgress')).toBe(false);
  });
});

describe('tidyNumber and rupees', () => {
  it('tidies a number the way the server does', () => {
    expect(tidyNumber('py 01-ab/1234')).toBe('PY01AB1234');
  });

  it('shows whole rupees plainly', () => {
    expect(rupees(203)).toBe('₹203');
    expect(rupees(203.5)).toBe('₹203.50');
  });
});

describe('rideErrorMessageKey', () => {
  it('maps what the server says, and falls back kindly', () => {
    expect(rideErrorMessageKey(new ApiError(409, 'ride_taken'))).toBe('rides.errors.ride_taken');
    expect(rideErrorMessageKey(new ApiError(409, 'ride_driver_insurance_ended'))).toBe(
      'rides.errors.ride_driver_insurance_ended',
    );
    expect(rideErrorMessageKey(new ApiError(500, 'http_500'))).toBe('rides.errors.generic');
    expect(rideErrorMessageKey(new NetworkError('timeout'))).toBe('rides.errors.timeout');
    expect(rideErrorMessageKey(new NetworkError('offline'))).toBe('rides.errors.offline');
  });
});
