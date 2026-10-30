import { StyleSheet, View } from 'react-native';

import { OPInput, type OPInputProps } from '../atoms/OPInput';
import { OPText } from '../atoms/OPText';
import { useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface FormFieldProps extends Omit<OPInputProps, 'hasError' | 'accessibilityLabel'> {
  label: string;
  hint?: string;
  /** Validation message; also announced to screen readers. */
  error?: string;
}

export function FormField({ label, hint, error, ...inputProps }: FormFieldProps) {
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.field}>
      <OPText variant="label">{label}</OPText>
      <OPInput
        {...inputProps}
        hasError={Boolean(error)}
        accessibilityLabel={label}
        accessibilityHint={error ?? hint}
      />
      {error ? (
        <OPText variant="caption" tone="error" accessibilityLiveRegion="polite">
          {error}
        </OPText>
      ) : hint ? (
        <OPText variant="caption" tone="secondary">
          {hint}
        </OPText>
      ) : null}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    field: {
      gap: theme.spacing.xxs,
    },
  });
