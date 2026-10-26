import { Children, Fragment, isValidElement, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';

import { OPDivider } from '../atoms/OPDivider';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface ListSectionProps {
  title?: string;
  children: ReactNode;
}

/** Grouped list card with an optional heading and dividers between rows. */
export function ListSection({ title, children }: ListSectionProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const rows = Children.toArray(children).filter(isValidElement);
  return (
    <View style={styles.section}>
      {title ? (
        <OPText variant="label" tone="secondary" accessibilityRole="header" style={styles.title}>
          {title}
        </OPText>
      ) : null}
      <View style={styles.card}>
        {rows.map((row, index) => (
          <Fragment key={row.key ?? index}>
            {index > 0 ? <OPDivider inset={theme.spacing.md} /> : null}
            {row}
          </Fragment>
        ))}
      </View>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    section: {
      gap: theme.spacing.xs,
      paddingHorizontal: theme.layout.screenPadding,
    },
    title: {
      paddingHorizontal: theme.spacing.xxs,
    },
    card: {
      overflow: 'hidden',
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
  });
