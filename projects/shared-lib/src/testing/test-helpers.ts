/**
 * Shared test helpers for shared-lib specs.
 * Import from here to avoid duplication across spec files.
 */
import { AuthUser } from '../lib/models';

/**
 * Build a minimal RFC-7519 JWT whose `exp` claim is `offsetSeconds` from now.
 * The signature is a stub — not cryptographically valid, but sufficient for
 * `hasValidToken()` which only decodes the payload.
 */
export function makeJwt(offsetSeconds: number): string {
  const header  = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + offsetSeconds }));
  const sig     = btoa('stub-signature');
  return `${header}.${payload}.${sig}`;
}

/** Build a minimal AuthUser fixture for testing role/auth scenarios.
 * `roles` are role NAMES (auth tier). */
export function fakeUser(roles: string[], overrides: Partial<AuthUser> = {}): AuthUser {
  return {
    id: 'u1',
    staffId: 'NC001',
    fullName: 'Test User',
    email: 't@test.com',
    roles,
    ...overrides,
  };
}
