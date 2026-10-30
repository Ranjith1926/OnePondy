import { z } from 'zod';

import { vehicleTypes } from '../api/driverApi';

// Messages are i18n keys; screens translate them when showing field errors.

/** Registration and licence numbers are compared without spaces, hyphens, slashes or case, as the API does. */
export function tidyIdentifier(value: string): string {
  return value.toUpperCase().replace(/[\s\-/]/g, '');
}

export const driverSchema = z
  .object({
    fullName: z.string().trim().min(2, 'driver.validation.name').max(100, 'driver.validation.name'),
    vehicleType: z.enum(vehicleTypes, {
      errorMap: () => ({ message: 'driver.validation.vehicleType' }),
    }),
    vehicleNumber: z.string(),
    licenceNumber: z.string(),
  })
  .superRefine((form, context) => {
    // A bicycle has no registration or licence; any typed before switching the vehicle are dropped.
    if (form.vehicleType === 'bicycle') {
      return;
    }
    if (!/^[A-Z0-9]{4,12}$/.test(tidyIdentifier(form.vehicleNumber))) {
      context.addIssue({
        code: 'custom',
        path: ['vehicleNumber'],
        message: 'driver.validation.vehicleNumber',
      });
    }
    if (!/^[A-Z0-9]{8,20}$/.test(tidyIdentifier(form.licenceNumber))) {
      context.addIssue({
        code: 'custom',
        path: ['licenceNumber'],
        message: 'driver.validation.licenceNumber',
      });
    }
  });

export type DriverForm = z.input<typeof driverSchema>;

export type DriverValidationKey =
  | 'driver.validation.name'
  | 'driver.validation.vehicleType'
  | 'driver.validation.vehicleNumber'
  | 'driver.validation.licenceNumber';

export function isDriverValidationKey(value: unknown): value is DriverValidationKey {
  return (
    value === 'driver.validation.name' ||
    value === 'driver.validation.vehicleType' ||
    value === 'driver.validation.vehicleNumber' ||
    value === 'driver.validation.licenceNumber'
  );
}
