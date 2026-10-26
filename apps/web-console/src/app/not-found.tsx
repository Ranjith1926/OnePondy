import Link from 'next/link';

export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-3 px-4 text-center">
      <h1 className="text-2xl font-bold">Not found</h1>
      <p className="text-muted">That page or item does not exist, or it was removed.</p>
      <Link href="/" className="font-semibold text-brand hover:underline">
        Back to the dashboard
      </Link>
    </main>
  );
}
