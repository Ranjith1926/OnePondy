/* eslint-disable @typescript-eslint/no-require-imports -- jest.mock factories are hoisted and must use require() */
import '@testing-library/react-native';

import './src/i18n';

jest.mock('@react-native-async-storage/async-storage', () =>
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);

jest.mock(
  'react-native-safe-area-context',
  () => require('react-native-safe-area-context/jest/mock').default,
);
