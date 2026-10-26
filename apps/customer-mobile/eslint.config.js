// https://docs.expo.dev/guides/using-eslint/
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');
const eslintPluginPrettierRecommended = require('eslint-plugin-prettier/recommended');

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
  {
    // Reducers and app state stay pure: no API, storage or navigation side effects (§6).
    files: ['src/state/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/api/**', '**/storage/**'],
              message: 'App state must not perform API or storage calls (spec §6).',
            },
          ],
          paths: [
            { name: 'expo-secure-store', message: 'No storage access in app state (spec §6).' },
            {
              name: '@react-native-async-storage/async-storage',
              message: 'No storage access in app state (spec §6).',
            },
            { name: '@react-navigation/native', message: 'No navigation in app state (spec §6).' },
          ],
        },
      ],
    },
  },
]);
