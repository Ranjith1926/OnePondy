import type { Metadata } from 'next';

import { Card, PageHeader } from '@/components/Display';

import { ApplyForm } from './ApplyForm';

export const metadata: Metadata = { title: 'Apply as a partner' };

export default function ApplyPage() {
  return (
    <>
      <PageHeader
        title="Apply as a partner"
        description="Tell us about your activity, stay, shop or service. We'll review it before it appears in the app."
      />
      <Card className="max-w-2xl">
        <ApplyForm />
      </Card>
    </>
  );
}
