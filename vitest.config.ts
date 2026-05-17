import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
    environment: 'jsdom',
    include: ['projects/**/*.spec.ts'],
    exclude: ['**/node_modules/**', '**/dist/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html', 'lcov'],
      reportsDirectory: './coverage',
      include: [
        'projects/shared-lib/src/**/*.ts',
        'projects/admin-app/src/**/*.ts',
        'projects/staff-app/src/**/*.ts',
      ],
      exclude: [
        '**/*.spec.ts',
        '**/main.ts',
        '**/environments/**',
        '**/*.config.ts',
        '**/public-api.ts',
        '**/*.module.ts',
      ],
      thresholds: {
        lines: 40,
        functions: 40,
        branches: 40,
        statements: 40,
      },
    },
  },
});
