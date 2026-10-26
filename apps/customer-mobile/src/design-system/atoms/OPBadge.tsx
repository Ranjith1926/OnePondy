import { StyleSheet, View } from 'react-native';

import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';
import { OPText } from './OPText';

export type BadgeTone = 'neutral' | 'brand' | 'accent' | 'success' | 'warning' | 'error' | 'info';

function toneColors(theme: Theme, tone: BadgeTone): { background: string; foreground: string } {
  const c = theme.colors;
  switch (tone) {
    case 'neutral':
      return { background: c.surfaceMuted, foreground: c.text.secondary };
    case 'brand':
      return { background: c.brand.primaryContainer, foreground: c.brand.onPrimaryContainer };
    case 'accent':
      return { background: c.brand.secondaryContainer, foreground: c.brand.onSecondaryContainer };
    case 'success':
      return { background: c.successContainer, foreground: c.onSuccessContainer };
    case 'warning':
      return { background: c.warningContainer, foreground: c.onWarningContainer };
    case 'error':
      return { background: c.errorContainer, foreground: c.onErrorContainer };
    case 'info':
      return { background: c.infoContainer, foreground: c.onInfoContainer };
  }
}

interface OPBadgeProps {
  label: string;
  tone?: BadgeTone;
}

export function OPBadge({ label, tone = 'neutral' }: OPBadgeProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const { background, foreground } = toneColors(theme, tone);
  return (
    <View style={[styles.badge, { backgroundColor: background }]}>
      <OPText variant="caption" style={{ color: foreground }} numberOfLines={1}>
        {label}
      </OPText>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    badge: {
      alignSelf: 'flex-start',
      paddingHorizontal: theme.spacing.xs,
      paddingVertical: 2,
      borderRadius: theme.radii.pill,
    },
  });
