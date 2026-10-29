import { apiBaseUrl } from '@/lib/config';
import { getPartnerSession } from '@/lib/session';

/** Streams a statement's CSV through the console, authenticated as the signed-in partner, so the browser never
 * needs the access token. The API answers 404 for anyone who does not own the business. */
export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string; statementId: string }> },
) {
  const session = await getPartnerSession();
  if (!session) {
    return new Response('Not found', { status: 404 });
  }

  const { id, statementId } = await params;
  const upstream = await fetch(
    `${apiBaseUrl()}/api/v1/partner/businesses/${encodeURIComponent(id)}/statements/${encodeURIComponent(statementId)}/csv`,
    { headers: { Authorization: `Bearer ${session.accessToken}` }, cache: 'no-store' },
  );
  if (!upstream.ok || !upstream.body) {
    return new Response('Not found', { status: upstream.status === 404 ? 404 : 502 });
  }

  return new Response(upstream.body, {
    headers: {
      'Content-Type': 'text/csv; charset=utf-8',
      // The API names the file after the period; pass that name on.
      'Content-Disposition':
        upstream.headers.get('Content-Disposition') ?? 'attachment; filename="statement.csv"',
      'Cache-Control': 'no-store',
    },
  });
}
