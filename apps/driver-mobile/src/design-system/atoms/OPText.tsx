import { Text, type TextProps } from 'react-native';

import { useTheme } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';
import type { TextVariant } from '../tokens/typography';

export type TextTone =
  | 'primary'
  | 'secondary'
  | 'disabled'
  | 'link'
  | 'onPrimary'
  | 'onPrimaryContainer'
  | 'error'
  | 'success';

function toneColor(theme: Theme, tone: TextTone): string {
  switch (tone) {
    case 'primary':
      return theme.colors.text.primary;
    case 'secondary':
      return theme.colors.text.secondary;
    case 'disabled':
      return theme.colors.text.disabled;
    case 'link':
      return theme.colors.text.link;
    case 'onPrimary':
      return theme.colors.brand.onPrimary;
    case 'onPrimaryContainer':
      return theme.colors.brand.onPrimaryContainer;
    case 'error':
      return theme.colors.error;
    case 'success':
      return theme.colors.success;
  }
}

export interface OPTextProps extends TextProps {
  variant?: TextVariant;
  tone?: TextTone;
  align?: 'auto' | 'left' | 'center' | 'right';
}

export function OPText({ variant = 'body', tone = 'primary', align, style, ...rest }: OPTextProps) {
  const theme = useTheme();
  return (
    <Text
      {...rest}
      style={[
        theme.typography[variant],
        { color: toneColor(theme, tone) },
        align ? { textAlign: align } : null,
        style,
      ]}
    />
  );
}
