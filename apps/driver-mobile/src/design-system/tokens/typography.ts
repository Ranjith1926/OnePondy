import { Platform, type TextStyle } from 'react-native';

// Plus Jakarta Sans is embedded at build time by the expo-font config plugin (app.json).
// Android registers it under the family name from app.json; iOS reads it from the font files.
// Placeholder brand typeface until final brand assets are chosen (spec D18).
export const fontFamily = Platform.select({
  android: 'PlusJakartaSans',
  default: 'Plus Jakarta Sans',
});

export type TextVariant =
  'display' | 'title' | 'heading' | 'subheading' | 'body' | 'bodyStrong' | 'label' | 'caption';

export const typography: Record<TextVariant, TextStyle> = {
  display: { fontFamily, fontSize: 32, lineHeight: 40, fontWeight: '800', letterSpacing: -0.5 },
  title: { fontFamily, fontSize: 24, lineHeight: 32, fontWeight: '700', letterSpacing: -0.3 },
  heading: { fontFamily, fontSize: 20, lineHeight: 28, fontWeight: '700', letterSpacing: -0.2 },
  subheading: { fontFamily, fontSize: 17, lineHeight: 24, fontWeight: '600' },
  body: { fontFamily, fontSize: 16, lineHeight: 24, fontWeight: '400' },
  bodyStrong: { fontFamily, fontSize: 16, lineHeight: 24, fontWeight: '600' },
  label: { fontFamily, fontSize: 14, lineHeight: 20, fontWeight: '600' },
  caption: { fontFamily, fontSize: 13, lineHeight: 18, fontWeight: '500' },
};
