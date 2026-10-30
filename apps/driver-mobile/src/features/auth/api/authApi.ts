import { apiRequest } from '../../../api/apiClient';
import type { SessionUser } from '../../../session/types';

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

/** Drivers sign in with the mobile number their account uses; the code is texted to it (spec D1, D19). */
export const authApi = {
  requestCode: (phone: string) =>
    apiRequest<OtpRequested>('/api/v1/auth/otp/request', {
      method: 'POST',
      body: { channel: 'sms', destination: phone },
    }),

  verifyCode: (challengeId: string, code: string, deviceName?: string) =>
    apiRequest<SessionTokens>('/api/v1/auth/otp/verify', {
      method: 'POST',
      body: { challengeId, code, deviceName },
    }),

  refresh: (refreshToken: string) =>
    apiRequest<SessionTokens>('/api/v1/auth/refresh', { method: 'POST', body: { refreshToken } }),

  signOut: () => apiRequest<void>('/api/v1/auth/logout', { method: 'POST', auth: true }),
};
