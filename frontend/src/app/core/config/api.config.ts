import { environment } from '../../../environments/environment';

const configuredApiBaseUrl = environment.apiBaseUrl.trim().replace(/\/+$/, '');

export function buildApiUrl(path: string): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  if (!configuredApiBaseUrl) {
    return normalizedPath;
  }

  return `${configuredApiBaseUrl}${normalizedPath}`;
}
