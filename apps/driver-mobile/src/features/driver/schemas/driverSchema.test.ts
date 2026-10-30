import { driverSchema, tidyIdentifier } from './driverSchema';

const scooter = {
  fullName: 'Ravi Kumar',
  vehicleType: 'scooter' as const,
  vehicleNumber: 'PY 01 AB 1234',
  licenceNumber: 'PY-0120200012345',
};

function issues(input: unknown): Record<string, string> {
  const result = driverSchema.safeParse(input);
  if (result.success) {
    return {};
  }
  return Object.fromEntries(
    result.error.issues.map((issue) => [issue.path.join('.'), issue.message]),
  );
}

describe('tidyIdentifier', () => {
  it('ignores case, spaces, hyphens and slashes, as the API does', () => {
    expect(tidyIdentifier('py 01-ab/1234')).toBe('PY01AB1234');
  });
});

describe('driverSchema', () => {
  it('accepts a scooter with a registration and a licence', () => {
    expect(issues(scooter)).toEqual({});
  });

  it('accepts a bicycle with no papers, and ignores any that were typed before switching', () => {
    expect(
      issues({ fullName: 'Meena S', vehicleType: 'bicycle', vehicleNumber: '', licenceNumber: '' }),
    ).toEqual({});
    expect(
      issues({ ...scooter, vehicleType: 'bicycle', vehicleNumber: '!', licenceNumber: '1' }),
    ).toEqual({});
  });

  it('wants a name of 2 to 100 characters after trimming', () => {
    expect(issues({ ...scooter, fullName: '  R ' })).toEqual({
      fullName: 'driver.validation.name',
    });
    expect(issues({ ...scooter, fullName: '' })).toEqual({ fullName: 'driver.validation.name' });
    expect(issues({ ...scooter, fullName: 'R'.repeat(101) })).toEqual({
      fullName: 'driver.validation.name',
    });
    expect(issues({ ...scooter, fullName: 'R'.repeat(100) })).toEqual({});
  });

  it('needs a vehicle to be chosen', () => {
    expect(issues({ ...scooter, vehicleType: undefined })).toEqual({
      vehicleType: 'driver.validation.vehicleType',
    });
    expect(issues({ ...scooter, vehicleType: 'helicopter' })).toEqual({
      vehicleType: 'driver.validation.vehicleType',
    });
  });

  it('wants a registration of 4 to 12 letters and digits for a motorised vehicle', () => {
    expect(issues({ ...scooter, vehicleNumber: 'A!' })).toEqual({
      vehicleNumber: 'driver.validation.vehicleNumber',
    });
    expect(issues({ ...scooter, vehicleNumber: '' })).toEqual({
      vehicleNumber: 'driver.validation.vehicleNumber',
    });
    expect(issues({ ...scooter, vehicleNumber: 'A'.repeat(13) })).toEqual({
      vehicleNumber: 'driver.validation.vehicleNumber',
    });
    expect(issues({ ...scooter, vehicleType: 'motorbike', vehicleNumber: 'TN 09 K 1' })).toEqual(
      {},
    );
  });

  it('wants a licence of 8 to 20 letters and digits for a motorised vehicle', () => {
    expect(issues({ ...scooter, licenceNumber: '123' })).toEqual({
      licenceNumber: 'driver.validation.licenceNumber',
    });
    expect(issues({ ...scooter, licenceNumber: 'L'.repeat(21) })).toEqual({
      licenceNumber: 'driver.validation.licenceNumber',
    });
  });
});
