import { StyleSheet, View } from 'react-native';

import { useTheme } from '../theme/ThemeProvider';

interface OPDividerProps {
  /** Left inset in dp, e.g. to align with text after a leading icon. */
  inset?: number;
}

export function OPDivider({ inset = 0 }: OPDividerProps) {
  const theme = useTheme();
  return (
    <View
      accessible={false}
      importantForAccessibility="no-hide-descendants"
      style={{
        height: StyleSheet.hairlineWidth,
        marginLeft: inset,
        backgroundColor: theme.colors.border,
      }}
    />
  );
}
