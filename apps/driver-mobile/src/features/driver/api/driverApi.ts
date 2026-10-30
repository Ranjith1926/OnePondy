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

export type JobStatus = 'open' | 'accepted' | 'pickedUp' | 'delivered' | 'cancelled';

/** An open delivery. It shows the area only: the customer's address and phone come once the driver has taken it. */
export interface JobOffer {
  id: string;
  restaurantName: string;
  restaurantAddress: string | null;
  deliveryArea: string;
  itemCount: number;
  openedAt: string;
}

/** The driver's own delivery, with what they need to collect and hand it over. */
export interface JobDetail {
  id: string;
  status: JobStatus;
  restaurantName: string;
  restaurantAddress: string | null;
  restaurantPhone: string | null;
  deliveryArea: string;
  deliveryAddress: string;
  deliveryNote: string | null;
  contactPhone: string;
  items: { name: string; quantity: number }[];
  acceptedAt: string | null;
  pickedUpAt: string | null;
}

export interface DriverJobs {
  current: JobDetail | null;
  open: JobOffer[];
  /** False while off duty or not approved, which is why `open` is then empty. */
  canAcceptJobs: boolean;
}

export const driverApi = {
  getMine: () => apiRequest<DriverProfile>('/api/v1/driver/me', { auth: true }),

  save: (details: DriverDetails) =>
    apiRequest<DriverProfile>('/api/v1/driver/me', { method: 'PUT', body: details, auth: true }),

  jobs: () => apiRequest<DriverJobs>('/api/v1/driver/jobs', { auth: true }),

  acceptJob: (id: string) =>
    apiRequest<JobDetail>(`/api/v1/driver/jobs/${id}/accept`, { method: 'POST', auth: true }),

  releaseJob: (id: string) =>
    apiRequest<JobDetail>(`/api/v1/driver/jobs/${id}/release`, { method: 'POST', auth: true }),

  pickUpJob: (id: string) =>
    apiRequest<JobDetail>(`/api/v1/driver/jobs/${id}/pickup`, { method: 'POST', auth: true }),

  deliverJob: (id: string) =>
    apiRequest<JobDetail>(`/api/v1/driver/jobs/${id}/deliver`, { method: 'POST', auth: true }),

  shareLocation: (id: string, latitude: number, longitude: number) =>
    apiRequest<void>(`/api/v1/driver/jobs/${id}/location`, {
      method: 'POST',
      body: { latitude, longitude },
      auth: true,
    }),

  setDuty: (onDuty: boolean, version: number) =>
    apiRequest<DriverProfile>('/api/v1/driver/duty', {
      method: 'POST',
      body: { onDuty, version },
      auth: true,
    }),
};
