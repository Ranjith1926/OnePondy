import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';

import { OPButton } from '../atoms/OPButton';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface SectionWithHeaderProps {
  title: string;
  actionLabel?: string;
  onAction?: () => void;
  /** Horizontal inset of the header row; children manage their own insets (e.g. carousels). */
  inset?: number;
  children: ReactNode;
}

export function SectionWithHeader({
  title,
  actionLabel,
  onAction,
  inset,
  children,
}: SectionWithHeaderProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.section}>
      <View style={[styles.header, { paddingHorizontal: inset ?? theme.layout.screenPadding }]}>
        <OPText variant="heading" accessibilityRole="header" style={styles.title}>
          {title}
        </OPText>
        {actionLabel && onAction ? (
          <OPButton label={actionLabel} onPress={onAction} variant="tertiary" size="sm" />
        ) : null}
      </View>
      {children}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    section: {
      gap: theme.spacing.sm,
    },
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: theme.spacing.sm,
    },
    title: {
      flexShrink: 1,
    },
  });
