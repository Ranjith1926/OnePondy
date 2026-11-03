/** Only ids shaped like the API's GUIDs are put into request paths; anything else is refused before a request is made. */
export function asGuid(value: FormDataEntryValue | null): string | null {
  const text = typeof value === 'string' ? value : '';
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(text) ? text : null;
}

/** A version read from a form. An empty field must not become 0 (`Number('')` is 0), so it is refused like any other bad value. */
export function asVersion(value: FormDataEntryValue | null): number | null {
  const text = typeof value === 'string' ? value.trim() : '';
  if (!/^\d{1,10}$/.test(text)) {
    return null;
  }

  const version = Number(text);
  return Number.isSafeInteger(version) ? version : null;
}
