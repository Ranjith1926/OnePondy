import {
  clampZoom,
  fitBounds,
  project,
  TILE_SIZE,
  tileUrl,
  unproject,
  visibleTiles,
  worldSize,
} from './mapMath';

const puducherry = { latitude: 11.934, longitude: 79.8366 };
const london = { latitude: 51.5074, longitude: -0.1278 };

describe('project and unproject', () => {
  it('put the middle of the world at the middle of the map', () => {
    const point = project({ latitude: 0, longitude: 0 }, 0);

    expect(point.x).toBeCloseTo(TILE_SIZE / 2, 6);
    expect(point.y).toBeCloseTo(TILE_SIZE / 2, 6);
  });

  it('find the tiles other maps give for known places', () => {
    // London at zoom 10 is tile 511/340 on OpenStreetMap; Puducherry at zoom 12 is tile 2956/1911.
    const l = project(london, 10);
    expect([Math.floor(l.x / TILE_SIZE), Math.floor(l.y / TILE_SIZE)]).toEqual([511, 340]);
    const p = project(puducherry, 12);
    expect([Math.floor(p.x / TILE_SIZE), Math.floor(p.y / TILE_SIZE)]).toEqual([2956, 1911]);
  });

  it('give back the position they were given, at any zoom', () => {
    for (const zoom of [3, 9, 15, 18]) {
      const back = unproject(project(puducherry, zoom), zoom);

      expect(back.latitude).toBeCloseTo(puducherry.latitude, 6);
      expect(back.longitude).toBeCloseTo(puducherry.longitude, 6);
    }
  });

  it('keep latitudes the square map cannot show inside it, and wrap longitude', () => {
    expect(project({ latitude: 90, longitude: 0 }, 2).y).toBeGreaterThanOrEqual(0);
    expect(unproject({ x: worldSize(2) + 10, y: 100 }, 2).longitude).toBeLessThan(0);
  });
});

describe('tileUrl', () => {
  it('fills in the zoom and the tile address', () => {
    expect(tileUrl('https://tile.example/{z}/{x}/{y}.png', 12, 2956, 1911)).toBe(
      'https://tile.example/12/2956/1911.png',
    );
  });
});

describe('visibleTiles', () => {
  it('covers the whole view, and the centre falls on the tile that holds it', () => {
    const tiles = visibleTiles(puducherry, 12, 400, 300);

    expect(tiles.length).toBeGreaterThanOrEqual(4);
    const covering = tiles.filter(
      (t) => t.left <= 200 && 200 < t.left + TILE_SIZE && t.top <= 150 && 150 < t.top + TILE_SIZE,
    );
    expect(covering).toHaveLength(1);
    expect([covering[0].x, covering[0].y]).toEqual([2956, 1911]);
    expect(Math.min(...tiles.map((t) => t.left))).toBeLessThanOrEqual(0);
    expect(Math.max(...tiles.map((t) => t.left)) + TILE_SIZE).toBeGreaterThanOrEqual(400);
    expect(Math.min(...tiles.map((t) => t.top))).toBeLessThanOrEqual(0);
    expect(Math.max(...tiles.map((t) => t.top)) + TILE_SIZE).toBeGreaterThanOrEqual(300);
  });

  it('adds a ring of spare tiles when asked', () => {
    expect(visibleTiles(puducherry, 12, 400, 300, 1).length).toBeGreaterThan(
      visibleTiles(puducherry, 12, 400, 300, 0).length,
    );
  });

  it('never asks for a tile above the top or below the bottom of the world', () => {
    const tiles = visibleTiles({ latitude: 84, longitude: 0 }, 2, 600, 600, 1);

    expect(tiles.every((t) => t.y >= 0 && t.y < 4)).toBe(true);
  });

  it('wraps to the other side of the date line, and keeps every tile key distinct', () => {
    const tiles = visibleTiles({ latitude: 0, longitude: 179.9 }, 3, 600, 300, 1);

    expect(tiles.every((t) => t.x >= 0 && t.x < 8)).toBe(true);
    expect(new Set(tiles.map((t) => t.key)).size).toBe(tiles.length);
    expect(tiles.some((t) => t.x === 0)).toBe(true);
  });

  it('places tiles on whole pixels so there are no seams', () => {
    const tiles = visibleTiles({ latitude: 11.93412345, longitude: 79.83667891 }, 14, 390, 260, 1);

    expect(tiles.every((t) => Number.isInteger(t.left) && Number.isInteger(t.top))).toBe(true);
  });
});

describe('fitBounds', () => {
  it('centres a single point at the single-point zoom', () => {
    expect(fitBounds([puducherry], 400, 300)).toEqual({ center: puducherry, zoom: 15 });
    expect(fitBounds([puducherry], 400, 300, { singleZoom: 30, maxZoom: 17 }).zoom).toBe(17);
  });

  it('shows every point, as large as the view allows', () => {
    const beach = { latitude: 11.9341, longitude: 79.8366 };
    const matrimandir = { latitude: 12.0071, longitude: 79.81 };
    const view = { width: 400, height: 300 };
    const fit = fitBounds([beach, matrimandir], view.width, view.height, { padding: 40 });

    for (const point of [beach, matrimandir]) {
      const at = project(point, fit.zoom);
      const middle = project(fit.center, fit.zoom);
      expect(Math.abs(at.x - middle.x)).toBeLessThanOrEqual(view.width / 2 - 40 + 1);
      expect(Math.abs(at.y - middle.y)).toBeLessThanOrEqual(view.height / 2 - 40 + 1);
    }
    // One zoom level closer would not fit.
    const closer = [beach, matrimandir].map((p) => project(p, fit.zoom + 1));
    const spanY = Math.max(...closer.map((p) => p.y)) - Math.min(...closer.map((p) => p.y));
    const spanX = Math.max(...closer.map((p) => p.x)) - Math.min(...closer.map((p) => p.x));
    expect(spanX > view.width - 80 || spanY > view.height - 80).toBe(true);
  });

  it('falls back to the widest zoom for points on opposite sides of the world', () => {
    const fit = fitBounds(
      [
        { latitude: 60, longitude: -170 },
        { latitude: -60, longitude: 170 },
      ],
      300,
      300,
      { minZoom: 2 },
    );

    expect(fit.zoom).toBe(2);
  });

  it('answers something sensible for no points', () => {
    expect(fitBounds([], 300, 300).zoom).toBe(2);
  });
});

describe('clampZoom', () => {
  it('keeps a zoom within the allowed range', () => {
    expect(clampZoom(1, 3, 18)).toBe(3);
    expect(clampZoom(25, 3, 18)).toBe(18);
    expect(clampZoom(12, 3, 18)).toBe(12);
  });
});
