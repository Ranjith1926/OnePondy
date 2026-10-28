import type {
  BusinessApprovalStatus,
  BusinessDocumentKind,
  BusinessType,
  DayOfWeek,
  MediaLicence,
  ModuleStatus,
  VerificationStatus,
  VisitorCategory,
} from './apiTypes';

export const statusLabels: Record<VerificationStatus, string> = {
  draft: 'Draft',
  pending: 'Pending review',
  verified: 'Verified',
  rejected: 'Rejected',
};

export const visitorCategoryLabels: Record<VisitorCategory, string> = {
  standard: 'Standard',
  indianAdult: 'Indian adult',
  foreignAdult: 'Foreign adult',
  child: 'Child',
  student: 'Student',
  senior: 'Senior citizen',
};

export const visitorCategories = Object.keys(visitorCategoryLabels) as VisitorCategory[];

export const licenceLabels: Record<MediaLicence, string> = {
  owned: 'Our own photo',
  partnerSupplied: 'Supplied by a partner',
  ccBy: 'Creative Commons BY',
  ccBySa: 'Creative Commons BY-SA',
  cc0: 'Public domain (CC0)',
  licensedStock: 'Licensed stock photo',
  other: 'Other licence',
};

/** Mirrors the API rule: credit is needed unless we own the photo or it is public domain. */
export const licenceNeedsCredit = (licence: MediaLicence): boolean =>
  licence !== 'owned' && licence !== 'cc0';

export const moduleStatusLabels: Record<ModuleStatus, string> = {
  available: 'Available',
  comingSoon: 'Coming soon',
  hidden: 'Hidden',
};

export const moduleLabels: Record<string, string> = {
  food: 'Food',
  rides: 'Rides',
  stays: 'Stays',
  activities: 'Activities',
  tripPlanner: 'Trip planner',
  notifications: 'Notifications',
  support: 'Help and support',
};

export const days: DayOfWeek[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
];

export const dayLabels: Record<DayOfWeek, string> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
};

export const auditActionLabels: Record<string, string> = {
  'place.created': 'Created',
  'place.updated': 'Edited',
  'place.submitted': 'Submitted for review',
  'place.verified': 'Verified',
  'place.rejected': 'Rejected',
  'place.published': 'Published',
  'place.unpublished': 'Unpublished',
  'place.review_confirmed': 'Confirmed still accurate',
  'media.added': 'Photo added',
  'media.updated': 'Photo details changed',
  'media.removed': 'Photo removed',
  'config.module_flag_changed': 'Module setting changed',
  'config.min_app_version_changed': 'Minimum app version changed',
  'staff.role_granted': 'Staff role granted',
  'staff.role_revoked': 'Staff access removed',
  'business.applied': 'Applied as a partner',
  'business.updated': 'Application edited',
  'business.approved': 'Approved',
  'business.rejected': 'Rejected',
  'business.suspended': 'Suspended',
  'business.reinstated': 'Reinstated',
  'business.document_added': 'Document added',
  'business.document_removed': 'Document removed',
};

export const businessTypeLabels: Record<BusinessType, string> = {
  restaurant: 'Restaurant',
  hotel: 'Hotel / stay',
  activity: 'Activity',
  shop: 'Shop',
  tourOperator: 'Tour operator',
  service: 'Service',
};

export const businessStatusLabels: Record<BusinessApprovalStatus, string> = {
  pending: 'Pending review',
  approved: 'Approved',
  rejected: 'Rejected',
  suspended: 'Suspended',
};

export const businessDocumentLabels: Record<BusinessDocumentKind, string> = {
  gstin: 'GSTIN certificate',
  fssaiLicence: 'FSSAI licence',
  other: 'Other document',
};

const dateTime = new Intl.DateTimeFormat('en-IN', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Kolkata',
});

const date = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeZone: 'Asia/Kolkata' });

/** Times are stored in UTC and shown in India time (spec §9). */
export const formatDateTime = (iso: string | null): string =>
  iso ? dateTime.format(new Date(iso)) : '—';

export const formatDate = (iso: string | null): string => (iso ? date.format(new Date(iso)) : '—');
