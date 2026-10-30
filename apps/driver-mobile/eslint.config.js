// https://docs.expo.dev/guides/using-eslint/
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');
const eslintPluginPrettierRecommended = require('eslint-plugin-prettier/recommended');
const i18next = require('eslint-plugin-i18next');

// Props that carry user-facing text; literal strings in them must come from the locale files.
const userFacingProps = [
  'accessibilityLabel',
  'accessibilityHint',
  'placeholder',
  'title',
  'label',
  'message',
  'caption',
  'subtitle',
  'actionLabel',
  'retryLabel',
  'closeLabel',
  'dismissLabel',
  'badgeLabel',
  'prefix',
  'hint',
];

module.exports = defineConfig([
  expoConfig,
  eslintPluginPrettierRecommended,
  {
    ignores: ['dist/*', 'android/*', 'ios/*', '.expo/*', 'coverage/*'],
  },
  {
    files: ['**/*.{ts,tsx}'],
    rules: {
      // Spec §15: do not use `any` to bypass type safety.
      '@typescript-eslint/no-explicit-any': 'error',
    },
  },
  {
    // No user-facing string outside the locale files (spec §10).
    files: ['src/**/*.tsx'],
    ignores: ['**/*.test.tsx', 'src/test-utils/**'],
    plugins: { i18next },
    rules: {
      'i18next/no-literal-string': [
        'error',
        {
          mode: 'jsx-only',
          'jsx-attributes': { include: [`^(${userFacingProps.join('|')})$`] },
          // Allow strings with no letters, such as separators.
          words: { exclude: ['^[^A-Za-z\\u00C0-\\u024F\\u0B80-\\u0BFF]*$'] },
          message: 'Move user-facing text to src/i18n/locales/en.json and use t().',
        },
      ],
    },
  },
  {
    // The design system stays domain-agnostic and never imports feature code (§5).
    files: ['src/design-system/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/features/**'],
              message: 'The design system must not import from features (spec §5).',
            },
          ],
        },
      ],
    },
  },
]);
