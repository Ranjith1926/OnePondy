import type { Metadata } from 'next';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api } from '@/lib/api';
import type { StaffMember } from '@/lib/apiTypes';
import { requireAdministrator } from '@/lib/session';

import { AddStaffForm, StaffRow } from './StaffForms';

export const metadata: Metadata = { title: 'Staff' };

const savedMessages: Record<string, string> = {
  added: 'Access given. They can sign in now.',
  changed: 'Role changed. They have been signed out and sign in again with the new role.',
  removed: 'Access removed. They have been signed out of the console.',
};

export default async function StaffPage({
  searchParams,
}: {
  searchParams: Promise<{ saved?: string }>;
}) {
  const user = await requireAdministrator();
  const { saved } = await searchParams;
  const staff = await api<StaffMember[]>('/api/v1/admin/staff');

  return (
    <>
      <PageHeader title="Staff" description="Who can use this console, and what they can do." />
      {saved && savedMessages[saved] ? (
        <div className="mb-6">
          <Alert kind="success">{savedMessages[saved]}</Alert>
        </div>
      ) : null}
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card title="Current staff">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Person
                  </th>
                  <th scope="col" className="py-2">
                    Role
                  </th>
                </tr>
              </thead>
              <tbody>
                {staff.map((member) => (
                  <StaffRow key={member.userId} member={member} isYou={member.userId === user.id} />
                ))}
              </tbody>
            </table>
          </div>
        </Card>
        <Card title="Add someone">
          <AddStaffForm />
        </Card>
      </div>
    </>
  );
}
