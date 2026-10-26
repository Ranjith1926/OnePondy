import { apiBaseUrl } from '@/lib/config';

/**
 * Serves place photos through the console's own address, so the browser never needs to reach the
 * API directly. Photos are public on the API too (they appear in the customer app).
 */
export async function GET(_request: Request, { params }: { params: Promise<{ path: string[] }> }) {
  const { path } = await params;
  if (path.some((segment) => segment === '..' || segment.includes('\\'))) {
    return new Response('Not found', { status: 404 });
  }

  const upstream = await fetch(`${apiBaseUrl()}/media/${path.map(encodeURIComponent).join('/')}`, {
    cache: 'no-store',
  });
  if (!upstream.ok || !upstream.body) {
    return new Response('Not found', { status: upstream.status === 404 ? 404 : 502 });
  }

  return new Response(upstream.body, {
    headers: {
      'Content-Type': upstream.headers.get('Content-Type') ?? 'application/octet-stream',
      'Cache-Control': upstream.headers.get('Cache-Control') ?? 'public, max-age=3600',
    },
  });
}
