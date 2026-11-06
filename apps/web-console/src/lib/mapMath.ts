// Web Mercator maths for a map made of 256-pixel square tiles (the scheme OpenStreetMap and most map servers use). Pure functions, so the
// map component only has to draw what these return.

export const TILE_SIZE = 256;

/** The most northerly and southerly latitudes the square world map covers. */
const MAX_LATITUDE = 85.0511287798;

export interface LatLng {
  latitude: number;
  longitude: number;
}

export interface Point {
  x: number;
  y: number;
}

export interface PlacedTile {
  /** Unique within one view, including the repeats that appear when the map wraps around the world. */
  key: string;
  z: number;
  x: number;
  y: number;
  /** Where the tile's top-left corner sits in the map view, in pixels. */
  left: number;
  top: number;
}

/** The whole world's width and height in pixels at a zoom level. */
export const worldSize = (zoom: number): number => TILE_SIZE * 2 ** zoom;

export const clampZoom = (zoom: number, minZoom: number, maxZoom: number): number =>
  Math.max(minZoom, Math.min(maxZoom, zoom));

/** A position on Earth as a pixel in the world map at a zoom level (x grows east, y grows south). */
export function project({ latitude, longitude }: LatLng, zoom: number): Point {
  const lat = Math.max(-MAX_LATITUDE, Math.min(MAX_LATITUDE, latitude));
  const sin = Math.sin((lat * Math.PI) / 180);
  const size = worldSize(zoom);
  return {
    x: ((longitude + 180) / 360) * size,
    y: (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * size,
  };
}

/** The reverse of {@link project}. Longitude is brought back into -180 to 180 when the map has wrapped. */
export function unproject({ x, y }: Point, zoom: number): LatLng {
  const size = worldSize(zoom);
  const longitude = (x / size) * 360 - 180;
  const n = Math.PI - (2 * Math.PI * y) / size;
  const latitude = (180 / Math.PI) * Math.atan(0.5 * (Math.exp(n) - Math.exp(-n)));
  return {
    latitude: Math.max(-MAX_LATITUDE, Math.min(MAX_LATITUDE, latitude)),
    longitude: ((((longitude + 180) % 360) + 360) % 360) - 180,
  };
}

/** Fills a tile address into a server's URL template, for example `https://tile.example/{z}/{x}/{y}.png`. */
export function tileUrl(template: string, z: number, x: number, y: number): string {
  return template.replace('{z}', String(z)).replace('{x}', String(x)).replace('{y}', String(y));
}

/**
 * The tiles that cover a view of the given size centred on a position, with where each one goes. `margin` adds extra rings of tiles
 * around the view, so a little dragging does not show gaps. Tiles above the top or below the bottom of the world do not exist and
 * are left out; going past the date line wraps to the tile on the other side.
 */
export function visibleTiles(
  center: LatLng,
  zoom: number,
  width: number,
  height: number,
  margin = 0,
): PlacedTile[] {
  const middle = project(center, zoom);
  const originX = middle.x - width / 2;
  const originY = middle.y - height / 2;
  const count = 2 ** zoom;

  const firstX = Math.floor(originX / TILE_SIZE) - margin;
  const lastX = Math.floor((originX + width) / TILE_SIZE) + margin;
  const firstY = Math.floor(originY / TILE_SIZE) - margin;
  const lastY = Math.floor((originY + height) / TILE_SIZE) + margin;

  const tiles: PlacedTile[] = [];
  for (let x = firstX; x <= lastX; x += 1) {
    for (let y = firstY; y <= lastY; y += 1) {
      if (y < 0 || y >= count) {
        continue;
      }
      tiles.push({
        key: `${zoom}/${x}/${y}`,
        z: zoom,
        x: ((x % count) + count) % count,
        y,
        left: Math.round(x * TILE_SIZE - originX),
        top: Math.round(y * TILE_SIZE - originY),
      });
    }
  }
  return tiles;
}

export interface FitOptions {
  /** Space kept clear around the points, in pixels. */
  padding?: number;
  minZoom?: number;
  maxZoom?: number;
  /** The zoom used when there is only one point to show. */
  singleZoom?: number;
}

/** The centre and zoom at which every point is visible, as large as the view allows. */
export function fitBounds(
  points: readonly LatLng[],
  width: number,
  height: number,
  { padding = 48, minZoom = 2, maxZoom = 18, singleZoom = 15 }: FitOptions = {},
): { center: LatLng; zoom: number } {
  if (points.length === 0) {
    return { center: { latitude: 0, longitude: 0 }, zoom: minZoom };
  }
  if (points.length === 1) {
    return { center: points[0], zoom: clampZoom(singleZoom, minZoom, maxZoom) };
  }

  const usableWidth = Math.max(1, width - 2 * padding);
  const usableHeight = Math.max(1, height - 2 * padding);

  for (let zoom = maxZoom; zoom >= minZoom; zoom -= 1) {
    const projected = points.map((point) => project(point, zoom));
    const xs = projected.map((p) => p.x);
    const ys = projected.map((p) => p.y);
    const spanX = Math.max(...xs) - Math.min(...xs);
    const spanY = Math.max(...ys) - Math.min(...ys);
    if (spanX <= usableWidth && spanY <= usableHeight) {
      return {
        center: unproject(
          {
            x: (Math.max(...xs) + Math.min(...xs)) / 2,
            y: (Math.max(...ys) + Math.min(...ys)) / 2,
          },
          zoom,
        ),
        zoom,
      };
    }
  }

  const projected = points.map((point) => project(point, minZoom));
  const xs = projected.map((p) => p.x);
  const ys = projected.map((p) => p.y);
  return {
    center: unproject(
      { x: (Math.max(...xs) + Math.min(...xs)) / 2, y: (Math.max(...ys) + Math.min(...ys)) / 2 },
      minZoom,
    ),
    zoom: minZoom,
  };
}
