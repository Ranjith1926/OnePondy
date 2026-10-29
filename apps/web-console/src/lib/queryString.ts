/** Builds a query string from filters and an optional page, leaving out anything empty. */
export function queryString(filters: Record<string, string | undefined>, page?: number): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries({ ...filters, page: page?.toString() })) {
    if (value) {
      params.set(key, value);
    }
  }
  return params.toString();
}
