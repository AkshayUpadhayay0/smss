import { secureSessionStore } from '../auth/storage';
import { env } from '../config/env';
import { createApiClient } from './client';

/** The app-wide API client. Feature endpoint modules call `api.request`; nothing else talks HTTP. */
export const api = createApiClient({ baseUrl: () => env.apiBaseUrl, store: secureSessionStore });
