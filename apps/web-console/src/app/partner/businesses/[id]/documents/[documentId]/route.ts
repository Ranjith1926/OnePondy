import { apiBaseUrl } from '@/lib/config';
import { getPartnerSession } from '@/lib/session';

/** Streams a business document through the console, authenticated as the signed-in partner, so the
 * browser never needs the access token or a direct, guessable API URL. */
export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string; documentId: string }> },
) {
  const session = await getPartnerSession();
  if (!session) {
    return new Response('Not found', { status: 404 });
  }

  const { id, documentId } = await params;
  const upstream = await fetch(
    `${apiBaseUrl()}/api/v1/partner/businesses/${encodeURIComponent(id)}/documents/${encodeURIComponent(documentId)}`,
    { headers: { Authorization: `Bearer ${session.accessToken}` }, cache: 'no-store' },
  );
  if (!upstream.ok || !upstream.body) {
    return new Response('Not found', { status: upstream.status === 404 ? 404 : 502 });
  }

  return new Response(upstream.body, {
    headers: { 'Content-Type': upstream.headers.get('Content-Type') ?? 'application/octet-stream' },
  });
}
