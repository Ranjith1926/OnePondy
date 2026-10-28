// Shapes returned by the OnePondy API (backend/src/OnePondy.Application). Enums arrive in camelCase.

export type VerificationStatus = 'draft' | 'pending' | 'verified' | 'rejected';

export type VisitorCategory =
  'standard' | 'indianAdult' | 'foreignAdult' | 'child' | 'student' | 'senior';

export type MediaLicence =
  'owned' | 'partnerSupplied' | 'ccBy' | 'ccBySa' | 'cc0' | 'licensedStock' | 'other';

export type ModuleStatus = 'available' | 'comingSoon' | 'hidden';

export type StaffRole = 'administrator' | 'content_editor' | 'support_agent';

export type DayOfWeek =
  'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

export interface UserRef {
  id: string;
  name: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface Category {
  code: string;
  name: string;
}

export interface PlaceSummary {
  id: string;
  name: string;
  slug: string;
  categoryCode: string;
  status: VerificationStatus;
  isPublished: boolean;
  isReviewDue: boolean;
  updatedAt: string;
  lastReviewedAt: string | null;
}

export interface PlaceCounts {
  draft: number;
  pending: number;
  verified: number;
  rejected: number;
  published: number;
  reviewDue: number;
}

export interface OpeningPeriod {
  day: DayOfWeek;
  opens: string;
  closes: string;
}

export interface OpeningHours {
  alwaysOpen: boolean;
  periods: OpeningPeriod[];
  notes: string | null;
}

export interface EntryFee {
  category: VisitorCategory;
  amount: number;
  currency?: string;
}

export interface PlaceTranslation {
  language: 'ta' | 'fr';
  name: string;
  description: string | null;
  visitorGuidance: string | null;
}

export interface MediaItem {
  id: string;
  altText: string;
  caption: string | null;
  licence: MediaLicence;
  attribution: string | null;
  sourceUrl: string | null;
  sortOrder: number;
  width: number;
  height: number;
  urls: Record<'large' | 'medium' | 'thumb', string>;
}

export interface PlaceDetail {
  id: string;
  slug: string;
  version: number;
  name: string;
  categoryCode: string;
  description: string | null;
  visitorGuidance: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  openingHours: OpeningHours | null;
  entryFees: EntryFee[];
  translations: PlaceTranslation[];
  status: VerificationStatus;
  isPublished: boolean;
  isReadyForReview: boolean;
  isReviewDue: boolean;
  verifiedBy: UserRef | null;
  verifiedAt: string | null;
  verificationSource: string | null;
  lastReviewedAt: string | null;
  reviewNote: string | null;
  createdAt: string;
  createdBy: UserRef;
  updatedAt: string;
  updatedBy: UserRef;
  media: MediaItem[];
}

/** The body of POST /admin/places and the "place" part of PUT /admin/places/{id}. */
export interface PlaceInput {
  name: string;
  categoryCode: string;
  description: string | null;
  visitorGuidance: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  openingHours: { alwaysOpen: boolean; periods: OpeningPeriod[]; notes: string | null } | null;
  entryFees: { category: VisitorCategory; amount: number }[];
  translations: {
    language: 'ta' | 'fr';
    name: string;
    description: string | null;
    visitorGuidance: string | null;
  }[];
}

export interface ModuleFlag {
  key: string;
  status: ModuleStatus;
  updatedAt: string | null;
}

export interface AdminConfig {
  modules: ModuleFlag[];
  minSupportedAppVersion: string;
}

export interface StaffMember {
  userId: string;
  displayName: string;
  email: string | null;
  role: StaffRole;
}

export interface AuditEntry {
  id: string;
  occurredAt: string;
  actor: UserRef | null;
  action: string;
  entityType: string;
  entityId: string;
  details: string | null;
}

export interface SessionTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: { id: string; displayName: string; roles: string[] };
}

export interface CodeRequested {
  challengeId: string;
  expiresAt: string;
  resendAfterSeconds: number;
  maskedDestination: string;
}

export type OtpChannel = 'sms' | 'email';

export interface CustomerProfile {
  id: string;
  displayName: string;
  phoneNumber: string | null;
  email: string | null;
  preferredLanguage: string;
  createdAt: string;
}

export type BusinessType = 'restaurant' | 'hotel' | 'activity' | 'shop' | 'tourOperator' | 'service';

export type BusinessApprovalStatus = 'pending' | 'approved' | 'rejected' | 'suspended';

export type BusinessDocumentKind = 'gstin' | 'fssaiLicence' | 'other';

export interface BusinessDocument {
  id: string;
  kind: BusinessDocumentKind;
  contentType: string;
  uploadedAt: string;
}

export interface BusinessSummary {
  id: string;
  name: string;
  businessType: BusinessType;
  approvalStatus: BusinessApprovalStatus;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface BusinessDetail {
  id: string;
  version: number;
  ownerUserId: string;
  name: string;
  businessType: BusinessType;
  description: string | null;
  phoneNumber: string | null;
  email: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  gstin: string | null;
  fssaiLicenseNumber: string | null;
  approvalStatus: BusinessApprovalStatus;
  isActive: boolean;
  reviewNote: string | null;
  documents: BusinessDocument[];
  createdAt: string;
  updatedAt: string;
}

/** The body of POST /partner/businesses and the "business" part of PUT /partner/businesses/{id}. */
export interface BusinessApplicationInput {
  name: string;
  businessType: BusinessType;
  description: string | null;
  phoneNumber: string | null;
  email: string | null;
  address: string | null;
  lat: number | null;
  lng: number | null;
  gstin: string | null;
  fssaiLicenseNumber: string | null;
}
