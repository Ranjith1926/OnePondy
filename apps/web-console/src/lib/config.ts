// Server configuration from environment variables (see .env.example). Not sent to the browser.

export function apiBaseUrl(): string {
  const url = process.env.ONEPONDY_API_URL ?? 'http://localhost:5080';
  return url.replace(/\/+$/, '');
}

export function secureCookies(): boolean {
  return process.env.NODE_ENV === 'production' && process.env.CONSOLE_SECURE_COOKIES !== 'false';
}
