import { apiBaseUrl } from '@/lib/config';
import { getSession } from '@/lib/session';

/** Streams a business document through the console, authenticated as the signed-in staff member. */
export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string; documentId: string }> },
) {
  const session = await getSession();
  if (!session) {
    return new Response('Not found', { status: 404 });
  }

  const { id, documentId } = await params;
  const upstream = await fetch(
    `${apiBaseUrl()}/api/v1/admin/businesses/${encodeURIComponent(id)}/documents/${encodeURIComponent(documentId)}`,
    { headers: { Authorization: `Bearer ${session.accessToken}` }, cache: 'no-store' },
  );
  if (!upstream.ok || !upstream.body) {
    return new Response('Not found', { status: upstream.status === 404 ? 404 : 502 });
  }

  return new Response(upstream.body, {
    headers: { 'Content-Type': upstream.headers.get('Content-Type') ?? 'application/octet-stream' },
  });
}
