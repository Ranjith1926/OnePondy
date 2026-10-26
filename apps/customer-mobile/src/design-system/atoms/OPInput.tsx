import { useState, type ComponentRef, type ReactNode, type Ref } from 'react';
import { StyleSheet, TextInput, View, type TextInputProps } from 'react-native';

import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';
import { OPIcon, type IconName } from './OPIcon';

export interface OPInputProps extends Omit<
  TextInputProps,
  'style' | 'placeholderTextColor' | 'ref'
> {
  leadingIcon?: IconName;
  trailing?: ReactNode;
  hasError?: boolean;
  ref?: Ref<ComponentRef<typeof TextInput>>;
}

export function OPInput({
  leadingIcon,
  trailing,
  hasError = false,
  editable = true,
  onFocus,
  onBlur,
  ref,
  ...rest
}: OPInputProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const [focused, setFocused] = useState(false);

  const borderColor = hasError
    ? theme.colors.error
    : focused
      ? theme.colors.brand.primary
      : theme.colors.borderStrong;

  return (
    <View
      style={[
        styles.container,
        { borderColor, borderWidth: focused || hasError ? 2 : 1 },
        !editable ? styles.disabled : null,
      ]}
    >
      {leadingIcon ? (
        <OPIcon name={leadingIcon} size={20} color={theme.colors.text.secondary} />
      ) : null}
      <TextInput
        {...rest}
        ref={ref}
        editable={editable}
        style={styles.input}
        placeholderTextColor={theme.colors.text.secondary}
        selectionColor={theme.colors.brand.primary}
        cursorColor={theme.colors.brand.primary}
        keyboardAppearance={theme.scheme}
        onFocus={(event) => {
          setFocused(true);
          onFocus?.(event);
        }}
        onBlur={(event) => {
          setFocused(false);
          onBlur?.(event);
        }}
      />
      {trailing}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    container: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xs,
      minHeight: theme.layout.minTouchTarget,
      paddingLeft: theme.spacing.sm,
      paddingRight: theme.spacing.xxs,
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.surface,
    },
    disabled: {
      backgroundColor: theme.colors.surfaceMuted,
    },
    input: {
      flex: 1,
      minHeight: 44,
      paddingVertical: theme.spacing.xs,
      color: theme.colors.text.primary,
      fontFamily: theme.typography.body.fontFamily,
      fontSize: theme.typography.body.fontSize,
    },
  });
