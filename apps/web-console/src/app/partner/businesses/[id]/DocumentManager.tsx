'use client';

import { useActionState } from 'react';

import { Button } from '@/components/Button';
import { Alert, Card } from '@/components/Display';
import { SelectField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { BusinessDocument, BusinessDocumentKind } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { businessDocumentLabels, formatDateTime } from '@/lib/labels';

import { removeDocument, uploadDocument } from './actions';

const kinds = Object.keys(businessDocumentLabels) as BusinessDocumentKind[];

export function DocumentManager({
  businessId,
  documents,
  canUpload,
}: {
  businessId: string;
  documents: BusinessDocument[];
  canUpload: boolean;
}) {
  const [state, action] = useActionState<FormState, FormData>(uploadDocument, { status: 'idle' });

  return (
    <Card title="Documents">
      <div className="flex flex-col gap-4">
        {documents.length === 0 ? (
          <p className="text-sm text-muted">No documents uploaded yet.</p>
        ) : (
          <ul className="flex flex-col gap-3 text-sm">
            {documents.map((document) => (
              <li
                key={document.id}
                className="flex flex-wrap items-center justify-between gap-2 border-t border-line pt-3 first:border-t-0 first:pt-0"
              >
                <div>
                  <p className="font-semibold">{businessDocumentLabels[document.kind]}</p>
                  <p className="text-xs text-muted">Uploaded {formatDateTime(document.uploadedAt)}</p>
                </div>
                <div className="flex items-center gap-3">
                  <a
                    href={`/partner/businesses/${businessId}/documents/${document.id}`}
                    target="_blank"
                    rel="noreferrer"
                    className="font-semibold text-brand hover:underline"
                  >
                    View
                  </a>
                  <form action={removeDocument}>
                    <input type="hidden" name="id" value={businessId} />
                    <input type="hidden" name="documentId" value={document.id} />
                    <Button type="submit" variant="danger">
                      Remove
                    </Button>
                  </form>
                </div>
              </li>
            ))}
          </ul>
        )}

        {canUpload ? (
          <form action={action} className="flex flex-col gap-3 border-t border-line pt-4" noValidate>
            <h3 className="font-bold">Upload a document</h3>
            <p className="text-sm text-muted">PDF, JPEG or PNG up to 10 MB.</p>
            <input type="hidden" name="id" value={businessId} />
            {state.status === 'error' && state.message ? <Alert kind="error">{state.message}</Alert> : null}
            <div className="flex flex-col gap-1">
              <label htmlFor="file" className="text-sm font-semibold">
                File
              </label>
              <input
                id="file"
                name="file"
                type="file"
                accept="application/pdf,image/jpeg,image/png"
                className="text-sm"
                required
              />
            </div>
            <SelectField id="kind" label="Document type" defaultValue="gstin">
              {kinds.map((kind) => (
                <option key={kind} value={kind}>
                  {businessDocumentLabels[kind]}
                </option>
              ))}
            </SelectField>
            <div>
              <SubmitButton pendingLabel="Uploading…">Upload document</SubmitButton>
            </div>
          </form>
        ) : null}
      </div>
    </Card>
  );
}
