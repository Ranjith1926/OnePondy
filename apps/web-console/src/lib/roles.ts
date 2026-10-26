import type { StaffRole } from './apiTypes';

export const roleLabels: Record<StaffRole, string> = {
  administrator: 'Administrator',
  content_editor: 'Content editor',
  support_agent: 'Support agent',
};

export const roleDescriptions: Record<StaffRole, string> = {
  administrator: 'Everything, including configuration, staff and the audit log.',
  content_editor: 'Create, edit, verify and publish places and photos.',
  support_agent: 'View places and configuration.',
};

// These only decide what the console shows; the API enforces the same rules (spec §12).
export const canEditContent = (role: StaffRole): boolean =>
  role === 'administrator' || role === 'content_editor';

export const isAdministrator = (role: StaffRole): boolean => role === 'administrator';

const rank: StaffRole[] = ['administrator', 'content_editor', 'support_agent'];

/** The highest staff role among a session's roles, or null for customers. */
export function staffRoleFrom(roles: readonly string[]): StaffRole | null {
  return rank.find((role) => roles.includes(role)) ?? null;
}
