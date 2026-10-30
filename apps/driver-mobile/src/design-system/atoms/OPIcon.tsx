import { Lucide } from '@react-native-vector-icons/lucide/static';
import type { ComponentProps } from 'react';

import { useTheme } from '../theme/ThemeProvider';

export type IconName = ComponentProps<typeof Lucide>['name'];

interface OPIconProps {
  name: IconName;
  size?: number;
  color?: string;
  /** Omit for decorative icons; they are then hidden from screen readers. */
  accessibilityLabel?: string;
}

export function OPIcon({ name, size = 24, color, accessibilityLabel }: OPIconProps) {
  const theme = useTheme();
  const decorative = accessibilityLabel === undefined;
  return (
    <Lucide
      name={name}
      size={size}
      color={color ?? theme.colors.text.primary}
      accessible={!decorative}
      accessibilityLabel={accessibilityLabel}
      accessibilityElementsHidden={decorative}
      importantForAccessibility={decorative ? 'no-hide-descendants' : 'yes'}
    />
  );
}
