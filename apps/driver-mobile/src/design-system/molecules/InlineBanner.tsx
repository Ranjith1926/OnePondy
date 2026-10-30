import { StyleSheet, View } from 'react-native';

import { OPIcon, type IconName } from '../atoms/OPIcon';
import { OPIconButton } from '../atoms/OPIconButton';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

export type BannerKind = 'info' | 'success' | 'warning' | 'error';

interface InlineBannerProps {
  kind: BannerKind;
  message: string;
  onDismiss?: () => void;
  dismissLabel?: string;
}

const icons: Record<BannerKind, IconName> = {
  info: 'info',
  success: 'circle-check',
  warning: 'triangle-alert',
  error: 'circle-alert',
};

function bannerColors(theme: Theme, kind: BannerKind) {
  const c = theme.colors;
  switch (kind) {
    case 'info':
      return { background: c.infoContainer, foreground: c.onInfoContainer };
    case 'success':
      return { background: c.successContainer, foreground: c.onSuccessContainer };
    case 'warning':
      return { background: c.warningContainer, foreground: c.onWarningContainer };
    case 'error':
      return { background: c.errorContainer, foreground: c.onErrorContainer };
  }
}

export function InlineBanner({ kind, message, onDismiss, dismissLabel }: InlineBannerProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const { background, foreground } = bannerColors(theme, kind);
  return (
    <View
      accessibilityRole={kind === 'error' ? 'alert' : undefined}
      accessibilityLiveRegion={kind === 'error' ? 'assertive' : 'polite'}
      style={[styles.banner, { backgroundColor: background }]}
    >
      <OPIcon name={icons[kind]} size={20} color={foreground} />
      <OPText variant="label" style={[styles.message, { color: foreground }]}>
        {message}
      </OPText>
      {onDismiss && dismissLabel ? (
        <OPIconButton icon="x" size="sm" accessibilityLabel={dismissLabel} onPress={onDismiss} />
      ) : null}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    banner: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
      minHeight: theme.layout.minTouchTarget,
      paddingLeft: theme.spacing.md,
      paddingRight: theme.spacing.xxs,
      paddingVertical: theme.spacing.xxs,
      borderRadius: theme.radii.md,
    },
    message: {
      flex: 1,
    },
  });
