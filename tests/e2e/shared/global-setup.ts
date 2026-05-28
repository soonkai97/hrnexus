import type { FullConfig } from '@playwright/test';

// No global pre-authentication — each spec handles its own login.
// If the test suite grows large, save auth storage state here to avoid redundant logins.
export default async function globalSetup(_config: FullConfig): Promise<void> {}
