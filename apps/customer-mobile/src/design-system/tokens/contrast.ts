// WCAG 2.x relative luminance and contrast ratio for opaque hex colours (#RRGGBB).

function channelToLinear(value: number): number {
  const srgb = value / 255;
  return srgb <= 0.04045 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4;
}

export function relativeLuminance(hex: string): number {
  const match = /^#([0-9a-f]{6})$/i.exec(hex);
  if (!match?.[1]) {
    throw new Error(`Expected an opaque #RRGGBB colour, received "${hex}".`);
  }
  const value = parseInt(match[1], 16);
  const r = channelToLinear((value >> 16) & 0xff);
  const g = channelToLinear((value >> 8) & 0xff);
  const b = channelToLinear(value & 0xff);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrastRatio(foreground: string, background: string): number {
  const a = relativeLuminance(foreground);
  const b = relativeLuminance(background);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}
