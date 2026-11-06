import Constants from 'expo-constants';

import type { TileSource } from '../design-system';

// Where the map pictures come from. OpenStreetMap's public servers are for light use only (their tile usage policy), so before a busy
// public launch EXPO_PUBLIC_MAP_TILE_URL is pointed at a commercial tile provider or tiles we host ourselves. Nothing else changes.
const defaultTileUrl = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';

export const tileUrlTemplate = process.env.EXPO_PUBLIC_MAP_TILE_URL ?? defaultTileUrl;

/** Public tile servers ask every app to say who is calling. */
const userAgent = `OnePondy/${Constants.expoConfig?.version ?? 'dev'} (driver app; +https://github.com/Ranjith1926/OnePondy)`;

export const tileSource: TileSource = {
  urlTemplate: tileUrlTemplate,
  headers: { 'User-Agent': userAgent },
  maxZoom: 18,
};

/** The credit OpenStreetMap's licence asks to see on every map, and where it links to. */
export const attributionUrl = 'https://www.openstreetmap.org/copyright';
