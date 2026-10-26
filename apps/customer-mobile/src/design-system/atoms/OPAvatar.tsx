import { View } from 'react-native';

import { useTheme } from '../theme/ThemeProvider';
import { OPIcon } from './OPIcon';
import { OPText } from './OPText';

interface OPAvatarProps {
  /** Shows initials when provided, otherwise a person icon. */
  name?: string;
  size?: number;
}

function initialsOf(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('');
}

/** Decorative: show the person's name as text next to it. */
export function OPAvatar({ name, size = 56 }: OPAvatarProps) {
  const theme = useTheme();
  const initials = name ? initialsOf(name) : '';
  return (
    <View
      accessible={false}
      importantForAccessibility="no-hide-descendants"
      style={{
        width: size,
        height: size,
        borderRadius: size / 2,
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: theme.colors.brand.primaryContainer,
      }}
    >
      {initials ? (
        <OPText variant="subheading" tone="onPrimaryContainer">
          {initials}
        </OPText>
      ) : (
        <OPIcon name="user-round" size={size / 2} color={theme.colors.brand.onPrimaryContainer} />
      )}
    </View>
  );
}
