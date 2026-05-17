/**
 * Playwright global setup — runs once before any test file.
 *
 * Feature flags are managed via the database for E2E tests.
 * To enable all flags in the test environment:
 *   docker exec nightclub-workforce-postgres-1 psql -U ncuser -d nightclub_workforce \
 *     -c 'UPDATE "FeatureFlags" SET "IsEnabled" = true, "UpdatedAt" = NOW();'
 *
 * The global setup is intentionally a no-op here — flag state is pre-set in
 * the DB so tests don't depend on the feature-pin-guarded API toggle endpoint.
 */
export default async function globalSetup(): Promise<void> {
  // No-op: feature flags enabled directly in DB before test run.
}
