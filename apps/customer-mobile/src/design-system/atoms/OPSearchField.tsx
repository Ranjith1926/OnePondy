import { OPIconButton } from './OPIconButton';
import { OPInput } from './OPInput';

interface OPSearchFieldProps {
  value: string;
  onChangeText: (text: string) => void;
  placeholder: string;
  /** Spoken name of the clear button. */
  clearLabel: string;
  accessibilityLabel?: string;
  autoFocus?: boolean;
  onSubmitEditing?: () => void;
  testID?: string;
}

export function OPSearchField({
  value,
  onChangeText,
  placeholder,
  clearLabel,
  accessibilityLabel,
  autoFocus,
  onSubmitEditing,
  testID,
}: OPSearchFieldProps) {
  return (
    <OPInput
      leadingIcon="search"
      value={value}
      onChangeText={onChangeText}
      placeholder={placeholder}
      accessibilityLabel={accessibilityLabel ?? placeholder}
      accessibilityRole="search"
      autoFocus={autoFocus}
      autoCorrect={false}
      returnKeyType="search"
      onSubmitEditing={onSubmitEditing}
      testID={testID}
      trailing={
        value ? (
          <OPIconButton
            icon="x"
            size="sm"
            accessibilityLabel={clearLabel}
            onPress={() => onChangeText('')}
          />
        ) : null
      }
    />
  );
}
