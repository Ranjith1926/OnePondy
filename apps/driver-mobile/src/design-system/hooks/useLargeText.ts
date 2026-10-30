import { useWindowDimensions } from 'react-native';

/**
 * True when the user's system font size is large enough that compact, side-by-side layouts
 * should reflow (for example, stacking a value under its label). Text is never capped here.
 */
export function useLargeText(threshold = 1.3): boolean {
  return useWindowDimensions().fontScale >= threshold;
}
