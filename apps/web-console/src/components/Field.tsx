import type {
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react';

const control =
  'w-full rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm text-ink placeholder:text-muted aria-[invalid=true]:border-danger';

interface FieldShellProps {
  id: string;
  label: string;
  hint?: string;
  errors?: string[];
  children: ReactNode;
}

/** Label, control, hint and errors, wired together for screen readers. */
export function FieldShell({ id, label, hint, errors, children }: FieldShellProps) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-sm font-semibold">
        {label}
      </label>
      {children}
      {hint ? (
        <p id={`${id}-hint`} className="text-xs text-muted">
          {hint}
        </p>
      ) : null}
      {errors?.length ? (
        <div id={`${id}-error`} className="text-xs font-medium text-danger">
          {errors.map((error) => (
            <p key={error}>{error}</p>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function describedBy(id: string, hint?: string, errors?: string[]) {
  return (
    [hint ? `${id}-hint` : null, errors?.length ? `${id}-error` : null].filter(Boolean).join(' ') ||
    undefined
  );
}

type Common = { id: string; label: string; hint?: string; errors?: string[] };

export function TextField({
  id,
  label,
  hint,
  errors,
  ...input
}: Common & InputHTMLAttributes<HTMLInputElement>) {
  return (
    <FieldShell id={id} label={label} hint={hint} errors={errors}>
      <input
        id={id}
        name={input.name ?? id}
        className={control}
        aria-invalid={errors?.length ? true : undefined}
        aria-describedby={describedBy(id, hint, errors)}
        {...input}
      />
    </FieldShell>
  );
}

export function TextArea({
  id,
  label,
  hint,
  errors,
  ...input
}: Common & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <FieldShell id={id} label={label} hint={hint} errors={errors}>
      <textarea
        id={id}
        name={input.name ?? id}
        rows={input.rows ?? 4}
        className={control}
        aria-invalid={errors?.length ? true : undefined}
        aria-describedby={describedBy(id, hint, errors)}
        {...input}
      />
    </FieldShell>
  );
}

export function SelectField({
  id,
  label,
  hint,
  errors,
  children,
  ...select
}: Common & SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <FieldShell id={id} label={label} hint={hint} errors={errors}>
      <select
        id={id}
        name={select.name ?? id}
        className={control}
        aria-invalid={errors?.length ? true : undefined}
        aria-describedby={describedBy(id, hint, errors)}
        {...select}
      >
        {children}
      </select>
    </FieldShell>
  );
}
