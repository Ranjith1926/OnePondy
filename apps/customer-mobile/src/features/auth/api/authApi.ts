import { apiRequest } from '../../../api/apiClient';
import type { Language, SessionUser } from '../../../state';

export type OtpChannel = 'sms' | 'email';

export interface OtpRequested {
  challengeId: string;
  expiresAt: string;
  resendAfterSeconds: number;
  maskedDestination: string;
}

export interface SessionTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: SessionUser;
  isNewUser: boolean;
}

export interface Profile {
  id: string;
  displayName: string;
  phoneNumber: string | null;
  email: string | null;
  preferredLanguage: Language;
  createdAt: string;
}

export const authApi = {
  requestCode: (channel: OtpChannel, destination: string) =>
    apiRequest<OtpRequested>('/api/v1/auth/otp/request', {
      method: 'POST',
      body: { channel, destination },
    }),

  verifyCode: (challengeId: string, code: string, deviceName?: string) =>
    apiRequest<SessionTokens>('/api/v1/auth/otp/verify', {
      method: 'POST',
      body: { challengeId, code, deviceName },
    }),

  refresh: (refreshToken: string) =>
    apiRequest<SessionTokens>('/api/v1/auth/refresh', { method: 'POST', body: { refreshToken } }),

  signOut: () => apiRequest<void>('/api/v1/auth/logout', { method: 'POST', auth: true }),

  getProfile: () => apiRequest<Profile>('/api/v1/profile', { auth: true }),

  updateProfile: (changes: { displayName?: string; preferredLanguage?: Language }) =>
    apiRequest<Profile>('/api/v1/profile', { method: 'PATCH', body: changes, auth: true }),

  requestDeletionCode: () =>
    apiRequest<OtpRequested>('/api/v1/account/deletion/code', { method: 'POST', auth: true }),

  confirmDeletion: (challengeId: string, code: string) =>
    apiRequest<void>('/api/v1/account/deletion/confirm', {
      method: 'POST',
      body: { challengeId, code },
      auth: true,
    }),
};
