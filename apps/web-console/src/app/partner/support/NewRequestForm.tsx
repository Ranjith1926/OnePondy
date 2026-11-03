'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { SelectField, TextArea, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { SupportTopic } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { supportTopicLabels } from '@/lib/labels';

import { createRequest } from './actions';

const topics = Object.keys(supportTopicLabels) as SupportTopic[];

/** A business owner's request to support, about one of their businesses. */
export function NewRequestForm({ businesses }: { businesses: { id: string; name: string }[] }) {
  const [state, action] = useActionState<FormState, FormData>(createRequest, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <form action={action} className="flex flex-col gap-4" noValidate>
      {state.status === 'error' && state.message && !state.fieldErrors ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <SelectField
        id="businessId"
        name="businessId"
        label="Which business is it about?"
        errors={errors.businessId}
      >
        {businesses.map((business) => (
          <option key={business.id} value={business.id}>
            {business.name}
          </option>
        ))}
      </SelectField>
      <SelectField
        id="topic"
        name="topic"
        label="What is it about?"
        defaultValue="business"
        errors={errors.topic}
      >
        {topics.map((topic) => (
          <option key={topic} value={topic}>
            {supportTopicLabels[topic]}
          </option>
        ))}
      </SelectField>
      <TextField
        id="subject"
        name="subject"
        label="Subject"
        hint="A few words, for example “Menu item will not save”."
        maxLength={120}
        errors={errors.subject}
      />
      <TextArea
        id="message"
        name="message"
        label="Tell us what happened"
        hint="Please do not include card numbers or passwords."
        rows={5}
        maxLength={2000}
        errors={errors.message}
      />
      <div>
        <SubmitButton pendingLabel="Sending…">Send request</SubmitButton>
      </div>
    </form>
  );
}
