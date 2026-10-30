import { apiRequest } from '../../../api/apiClient';

export const vehicleTypes = ['bicycle', 'scooter', 'motorbike'] as const;
export type VehicleType = (typeof vehicleTypes)[number];

export type DriverApprovalStatus = 'pending' | 'approved' | 'rejected' | 'suspended';

/** The driver's own view of their application and duty. Everything but `configured` is empty until they apply. */
export interface DriverProfile {
  configured: boolean;
  userId: string;
  fullName: string | null;
  vehicleType: VehicleType | null;
  vehicleNumber: string | null;
  licenceNumber: string | null;
  approvalStatus: DriverApprovalStatus | null;
  reviewNote: string | null;
  isOnDuty: boolean;
  onDutySince: string | null;
  canGoOnDuty: boolean;
  version: number;
}

export interface DriverDetails {
  fullName: string;
  vehicleType: VehicleType;
  vehicleNumber: string | null;
  licenceNumber: string | null;
  /** Required once the driver has applied: the version they were looking at. */
  version?: number;
}

export const driverApi = {
  getMine: () => apiRequest<DriverProfile>('/api/v1/driver/me', { auth: true }),

  save: (details: DriverDetails) =>
    apiRequest<DriverProfile>('/api/v1/driver/me', { method: 'PUT', body: details, auth: true }),

  setDuty: (onDuty: boolean, version: number) =>
    apiRequest<DriverProfile>('/api/v1/driver/duty', {
      method: 'POST',
      body: { onDuty, version },
      auth: true,
    }),
};
