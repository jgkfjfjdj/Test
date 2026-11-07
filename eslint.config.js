// Flat ESLint config for all TypeScript in the monorepo.
import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import { defineConfig } from 'eslint/config';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default defineConfig(
  {
    ignores: [
      '**/node_modules/**',
      '**/dist/**',
      '**/coverage/**',
      'game/**',
      'sim/**',
      'docs/**',
      'packages/db/migrations/**',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    languageOptions: {
      globals: { ...globals.node },
    },
    rules: {
      '@typescript-eslint/consistent-type-imports': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      'no-console': 'error',
      eqeqeq: ['error', 'always'],
    },
  },
  {
    files: ['**/scripts/**', '**/cli/**', '**/*.config.{js,ts}', '**/build.mjs'],
    rules: { 'no-console': 'off' },
  },
  prettier,
);
