import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { OPIcon, type IconName } from '../atoms/OPIcon';
import { OPRadio } from '../atoms/OPRadio';
import { OPText } from '../atoms/OPText';
import { useLargeText } from '../hooks/useLargeText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

type Trailing =
  | { kind: 'chevron' }
  | { kind: 'value'; text: string }
  | { kind: 'radio'; selected: boolean }
  | { kind: 'custom'; node: ReactNode };

interface ListItemProps {
  title: string;
  subtitle?: string;
  leadingIcon?: IconName;
  trailing?: Trailing;
  onPress?: () => void;
  disabled?: boolean;
  accessibilityHint?: string;
  testID?: string;
}

export function ListItem({
  title,
  subtitle,
  leadingIcon,
  trailing,
  onPress,
  disabled = false,
  accessibilityHint,
  testID,
}: ListItemProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const largeText = useLargeText();
  const isRadio = trailing?.kind === 'radio';
  const valueText = trailing?.kind === 'value' ? trailing.text : undefined;
  const label = [title, subtitle, valueText].filter(Boolean).join(', ');
  // With large system text the value moves under the title so neither has to truncate.
  const stackValue = valueText !== undefined && largeText;

  return (
    <Pressable
      accessible
      accessibilityRole={isRadio ? 'radio' : onPress ? 'button' : undefined}
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{
        disabled,
        ...(isRadio ? { checked: trailing.selected } : {}),
      }}
      disabled={disabled || !onPress}
      onPress={onPress}
      testID={testID}
      style={({ pressed }) => [styles.row, pressed && onPress ? styles.pressed : null]}
    >
      {leadingIcon ? (
        <View style={styles.leading}>
          <OPIcon
            name={leadingIcon}
            size={20}
            color={disabled ? theme.colors.text.disabled : theme.colors.brand.primary}
          />
        </View>
      ) : null}
      <View style={styles.text}>
        <OPText tone={disabled ? 'disabled' : 'primary'}>{title}</OPText>
        {subtitle ? (
          <OPText variant="caption" tone={disabled ? 'disabled' : 'secondary'}>
            {subtitle}
          </OPText>
        ) : null}
        {stackValue ? (
          <OPText variant="label" tone="secondary">
            {valueText}
          </OPText>
        ) : null}
      </View>
      {trailing?.kind === 'chevron' || (stackValue && onPress) ? (
        <OPIcon name="chevron-right" size={20} color={theme.colors.text.secondary} />
      ) : null}
      {trailing?.kind === 'value' && !stackValue ? (
        <View style={styles.valueRow}>
          <OPText variant="label" tone="secondary" numberOfLines={2} style={styles.value}>
            {trailing.text}
          </OPText>
          {onPress ? (
            <OPIcon name="chevron-right" size={20} color={theme.colors.text.secondary} />
          ) : null}
        </View>
      ) : null}
      {trailing?.kind === 'radio' ? (
        <OPRadio selected={trailing.selected} disabled={disabled} />
      ) : null}
      {trailing?.kind === 'custom' ? trailing.node : null}
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
      minHeight: 56,
      paddingHorizontal: theme.spacing.md,
      paddingVertical: theme.spacing.xs,
    },
    pressed: {
      backgroundColor: theme.colors.surfaceMuted,
    },
    leading: {
      width: 36,
      height: 36,
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.brand.primaryContainer,
    },
    text: {
      flex: 1,
      gap: 2,
    },
    valueRow: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xxs,
      maxWidth: '45%',
    },
    value: {
      flexShrink: 1,
      textAlign: 'right',
    },
  });
