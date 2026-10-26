import { View } from 'react-native';

import { useTheme } from '../theme/ThemeProvider';

interface OPRadioProps {
  selected: boolean;
  disabled?: boolean;
}

/** Visual radio indicator. The row that contains it carries the radio role and checked state. */
export function OPRadio({ selected, disabled = false }: OPRadioProps) {
  const theme = useTheme();
  const color = disabled
    ? theme.colors.text.disabled
    : selected
      ? theme.colors.brand.primary
      : theme.colors.borderStrong;
  return (
    <View
      accessible={false}
      importantForAccessibility="no-hide-descendants"
      style={{
        width: 22,
        height: 22,
        borderRadius: 11,
        borderWidth: 2,
        borderColor: color,
        alignItems: 'center',
        justifyContent: 'center',
      }}
    >
      {selected ? (
        <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: color }} />
      ) : null}
    </View>
  );
}
