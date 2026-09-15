// Guards are implemented alongside the auth domain in `core/auth/auth.guard.ts`
// (auth state and its guards are tightly coupled). This barrel re-exports them
// so other parts of the app can still `import { authGuard } from 'core/guards'`.
export { authGuard, guestGuard } from '../auth/auth.guard';
