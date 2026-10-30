import { ActivityIndicator } from 'react-native';

import { useTheme } from '../theme/ThemeProvider';

interface OPSpinnerProps {
  size?: 'small' | 'large';
  accessibilityLabel?: string;
}

export function OPSpinner({ size = 'large', accessibilityLabel }: OPSpinnerProps) {
  const theme = useTheme();
  return (
    <ActivityIndicator
      size={size}
      color={theme.colors.brand.primary}
      accessibilityLabel={accessibilityLabel}
    />
  );
}
