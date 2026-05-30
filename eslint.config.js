// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = tseslint.config(
  {
    ignores: ['dist/**', 'coverage/**', '.angular/**', 'node_modules/**', 'tests/e2e/**'],
  },
  {
    files: ['projects/**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      // This workspace uses two component prefixes: 'app' (the two apps) and 'nc' (shared-lib).
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: ['app', 'nc'], style: 'kebab-case' }],
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: ['app', 'nc'], style: 'camelCase' }],
      // `any` is discouraged but not build-breaking — flagged for awareness (mostly in test mocks).
      '@typescript-eslint/no-explicit-any': 'warn',
      // Constructor DI is still used in places; migrating to inject() is a gradual style change.
      '@angular-eslint/prefer-inject': 'warn',
      // Off: conflicts with TS definite-assignment (a `let x = ''` default before branch assignment
      // is required so the variable is always assigned), which this rule flags as "useless".
      'no-useless-assignment': 'off',
    },
  },
  {
    files: ['projects/**/*.html'],
    extends: [
      ...angular.configs.templateRecommended,
      ...angular.configs.templateAccessibility,
    ],
    rules: {
      // Accessibility rules are valuable but surface 160+ existing findings — keep them visible as
      // warnings for gradual improvement rather than blocking the build on day one.
      '@angular-eslint/template/label-has-associated-control': 'warn',
      '@angular-eslint/template/click-events-have-key-events': 'warn',
      '@angular-eslint/template/interactive-supports-focus': 'warn',
    },
  },
);
