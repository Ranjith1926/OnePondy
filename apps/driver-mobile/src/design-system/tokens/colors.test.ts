import { colorTokens, type ColorScheme, type ColorTokens } from './colors';
import { contrastRatio } from './contrast';

// WCAG 2.2 AA: 4.5:1 for normal text, 3:1 for UI component boundaries (spec §4, §20).
const TEXT = 4.5;
const NON_TEXT = 3;

type Pair = [label: string, foreground: string, background: string, minimum: number];

function pairs(c: ColorTokens): Pair[] {
  return [
    ['text.primary on background', c.text.primary, c.background, TEXT],
    ['text.primary on surface', c.text.primary, c.surface, TEXT],
    ['text.primary on surfaceMuted', c.text.primary, c.surfaceMuted, TEXT],
    ['text.secondary on background', c.text.secondary, c.background, TEXT],
    ['text.secondary on surface', c.text.secondary, c.surface, TEXT],
    ['text.secondary on surfaceMuted', c.text.secondary, c.surfaceMuted, TEXT],
    ['text.link on background', c.text.link, c.background, TEXT],
    ['text.link on surface', c.text.link, c.surface, TEXT],
    ['brand.primary on surface (active tab label)', c.brand.primary, c.surface, TEXT],
    ['brand.onPrimary on brand.primary', c.brand.onPrimary, c.brand.primary, TEXT],
    ['brand.onPrimary on brand.primaryPressed', c.brand.onPrimary, c.brand.primaryPressed, TEXT],
    [
      'brand.onPrimaryContainer on brand.primaryContainer',
      c.brand.onPrimaryContainer,
      c.brand.primaryContainer,
      TEXT,
    ],
    ['brand.onSecondary on brand.secondary', c.brand.onSecondary, c.brand.secondary, TEXT],
    [
      'brand.onSecondaryContainer on brand.secondaryContainer',
      c.brand.onSecondaryContainer,
      c.brand.secondaryContainer,
      TEXT,
    ],
    ['onSuccessContainer on successContainer', c.onSuccessContainer, c.successContainer, TEXT],
    ['onWarningContainer on warningContainer', c.onWarningContainer, c.warningContainer, TEXT],
    ['onErrorContainer on errorContainer', c.onErrorContainer, c.errorContainer, TEXT],
    ['onInfoContainer on infoContainer', c.onInfoContainer, c.infoContainer, TEXT],
    ['error text on surface', c.error, c.surface, TEXT],
    ['error text on background', c.error, c.background, TEXT],
    ['success on surface', c.success, c.surface, TEXT],
    ['warning on surface', c.warning, c.surface, TEXT],
    ['info on surface', c.info, c.surface, TEXT],
    ['borderStrong on surface (input outline)', c.borderStrong, c.surface, NON_TEXT],
    ['borderStrong on background (input outline)', c.borderStrong, c.background, NON_TEXT],
    ['brand.primary on background (selected control)', c.brand.primary, c.background, NON_TEXT],
  ];
}

describe.each<ColorScheme>(['light', 'dark'])('%s colour tokens', (scheme) => {
  it.each(pairs(colorTokens[scheme]))('%s meets its contrast minimum', (_, fg, bg, minimum) => {
    expect(contrastRatio(fg, bg)).toBeGreaterThanOrEqual(minimum);
  });
});

describe('contrastRatio', () => {
  it('matches the WCAG reference values', () => {
    expect(contrastRatio('#000000', '#FFFFFF')).toBeCloseTo(21, 5);
    expect(contrastRatio('#777777', '#FFFFFF')).toBeCloseTo(4.48, 2);
  });

  it('rejects colours with transparency', () => {
    expect(() => contrastRatio('rgba(0, 0, 0, 0.5)', '#FFFFFF')).toThrow(/opaque/);
  });
});
