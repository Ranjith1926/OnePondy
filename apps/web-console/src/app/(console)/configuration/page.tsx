import type { Metadata } from 'next';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api } from '@/lib/api';
import type { AdminConfig } from '@/lib/apiTypes';
import { isAdministrator } from '@/lib/roles';
import { requireUser } from '@/lib/session';

import { MinVersionForm, ModuleFlagForm } from './ConfigForms';

export const metadata: Metadata = { title: 'Configuration' };

export default async function ConfigurationPage({
  searchParams,
}: {
  searchParams: Promise<{ saved?: string }>;
}) {
  const user = await requireUser();
  const { saved } = await searchParams;
  const config = await api<AdminConfig>('/api/v1/admin/config');
  const canChange = isAdministrator(user.role);

  return (
    <>
      <PageHeader
        title="Configuration"
        description="What the customer app shows for each service, and the oldest app version still allowed. Changes reach the app within a few minutes, without a new release."
      />
      {saved ? (
        <div className="mb-6">
          <Alert kind="success">
            Saved. The app picks this up the next time it checks, usually within minutes.
          </Alert>
        </div>
      ) : null}
      {!canChange ? (
        <div className="mb-6">
          <Alert kind="info">Only administrators can change configuration.</Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-2">
        <Card title="Services in the app">
          <p className="mb-2 text-sm text-muted">
            Never mark a service available before its partners and operations are ready. Hidden
            services do not appear at all; coming soon shows them with an explanation.
          </p>
          {config.modules.map((flag) => (
            <ModuleFlagForm key={flag.key} flag={flag} canChange={canChange} />
          ))}
        </Card>
        <Card title="App version">
          <p className="mb-4 text-sm text-muted">
            Phones running an older version are asked to update before they can continue. Raise this
            only after the new version is live in the stores.
          </p>
          <MinVersionForm version={config.minSupportedAppVersion} canChange={canChange} />
        </Card>
      </div>
    </>
  );
}
