import { fireEvent, screen, userEvent } from '@testing-library/react-native';
import { Linking } from 'react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { project, TILE_SIZE } from './mapMath';
import { TileMap, type MapMarker, type TileMapLabels, type TileSource } from './TileMap';

const tileSource: TileSource = {
  urlTemplate: 'https://tiles.example/{z}/{x}/{y}.png',
  headers: { 'User-Agent': 'OnePondy test' },
  maxZoom: 18,
};
const labels: TileMapLabels = {
  zoomIn: 'Zoom in',
  zoomOut: 'Zoom out',
  attribution: '© OpenStreetMap contributors',
  attributionHint: 'Opens the map credits',
};
const beach: MapMarker = {
  id: 'beach',
  latitude: 11.9341,
  longitude: 79.8366,
  label: 'Promenade Beach',
};
const matrimandir: MapMarker = {
  id: 'matri',
  latitude: 12.0071,
  longitude: 79.81,
  label: 'Matrimandir',
  tone: 'accent',
};

async function renderMap(props: Partial<React.ComponentProps<typeof TileMap>> = {}) {
  return renderWithProviders(
    <TileMap
      markers={[beach]}
      tileSource={tileSource}
      labels={labels}
      accessibilityLabel="Map of places"
      attributionUrl="https://www.openstreetmap.org/copyright"
      height={300}
      {...props}
    />,
  );
}

const tileSources = () =>
  screen
    .getAllByTestId('map-tile', { includeHiddenElements: true })
    .map((tile) => tile.props.source as { uri: string; headers?: Record<string, string> });
const zoomOfTiles = () => Number(tileSources()[0].uri.split('/').slice(-3)[0]);

const touch = (pageX: number, pageY: number) => ({ pageX, pageY });
const gestureEvent = (...touches: { pageX: number; pageY: number }[]) => ({
  nativeEvent: { touches, ...touches[0] },
});

describe('<TileMap />', () => {
  it('draws map tiles from the tile server, naming the app in every request', async () => {
    await renderMap();

    const sources = tileSources();
    expect(sources.length).toBeGreaterThanOrEqual(4);
    expect(sources.every((s) => /^https:\/\/tiles\.example\/\d+\/\d+\/\d+\.png$/.test(s.uri))).toBe(
      true,
    );
    expect(sources.every((s) => s.headers?.['User-Agent'] === 'OnePondy test')).toBe(true);
    // One place: the view centres on it at street level, so the tile that holds it is among those drawn.
    expect(zoomOfTiles()).toBe(15);
    const where = project(beach, 15);
    const holding = `https://tiles.example/15/${Math.floor(where.x / TILE_SIZE)}/${Math.floor(where.y / TILE_SIZE)}.png`;
    expect(sources.map((s) => s.uri)).toContain(holding);
  });

  it('names the map for screen readers, and keeps the tile pictures out of the way', async () => {
    await renderMap();

    expect(screen.getByLabelText('Map of places')).toBeOnTheScreen();
    expect(screen.queryAllByTestId('map-tile')).toHaveLength(0);
  });

  it('shows each place as a pin with its name, and reports which pin was pressed', async () => {
    const onMarkerPress = jest.fn();
    await renderMap({ markers: [beach, matrimandir], onMarkerPress });

    expect(screen.getByText('Promenade Beach')).toBeOnTheScreen();
    expect(screen.getByText('Matrimandir')).toBeOnTheScreen();
    await userEvent.setup().press(screen.getByRole('button', { name: 'Matrimandir' }));

    expect(onMarkerPress).toHaveBeenCalledWith('matri');
  });

  it('fits every pin into the view', async () => {
    await renderMap({ markers: [beach, matrimandir] });

    expect(zoomOfTiles()).toBeLessThan(15);
    expect(screen.getByTestId('map-marker-beach')).toBeOnTheScreen();
    expect(screen.getByTestId('map-marker-matri')).toBeOnTheScreen();
  });

  it('zooms with the plus and minus buttons, and stops at the limits', async () => {
    const user = userEvent.setup();
    await renderMap({ zoom: 17 });

    expect(zoomOfTiles()).toBe(17);
    await user.press(screen.getByRole('button', { name: 'Zoom in' }));
    expect(zoomOfTiles()).toBe(18);
    expect(screen.getByRole('button', { name: 'Zoom in' })).toBeDisabled();
    await user.press(screen.getByRole('button', { name: 'Zoom out' }));
    await user.press(screen.getByRole('button', { name: 'Zoom out' }));
    expect(zoomOfTiles()).toBe(16);
  });

  it('is only a picture with pins when it is not interactive: no zoom buttons, and touches are not taken', async () => {
    await renderMap({ interactive: false });

    expect(screen.queryByRole('button', { name: 'Zoom in' })).not.toBeOnTheScreen();
    const map = screen.getByTestId('tile-map');
    expect(map.props.onStartShouldSetResponder()).toBe(false);
  });

  it('moves with a drag', async () => {
    await renderMap({ zoom: 15 });
    const before = tileSources()
      .map((s) => s.uri)
      .join();
    const map = screen.getByTestId('tile-map');

    await fireEvent(map, 'responderGrant', gestureEvent(touch(200, 150)));
    await fireEvent(map, 'responderMove', gestureEvent(touch(-400, 150)));
    await fireEvent(map, 'responderRelease', gestureEvent());

    expect(
      tileSources()
        .map((s) => s.uri)
        .join(),
    ).not.toBe(before);
    expect(zoomOfTiles()).toBe(15);
  });

  it('zooms in a level when two fingers spread apart, and out when they come together', async () => {
    await renderMap({ zoom: 15 });
    const map = screen.getByTestId('tile-map');

    await fireEvent(map, 'responderGrant', gestureEvent(touch(100, 150)));
    await fireEvent(map, 'responderMove', gestureEvent(touch(100, 150), touch(200, 150)));
    await fireEvent(map, 'responderMove', gestureEvent(touch(50, 150), touch(250, 150)));
    await fireEvent(map, 'responderRelease', gestureEvent());
    expect(zoomOfTiles()).toBe(16);

    await fireEvent(map, 'responderGrant', gestureEvent(touch(100, 150)));
    await fireEvent(map, 'responderMove', gestureEvent(touch(50, 150), touch(250, 150)));
    // From 200 pixels apart to 100: half the distance, one level out.
    await fireEvent(map, 'responderMove', gestureEvent(touch(75, 150), touch(175, 150)));
    await fireEvent(map, 'responderRelease', gestureEvent());
    expect(zoomOfTiles()).toBe(15);
  });

  it('starts again from the pins when the caller changes the view key', async () => {
    const user = userEvent.setup();
    const { rerender } = await renderMap({ zoom: 15, viewKey: 'a' });
    await user.press(screen.getByRole('button', { name: 'Zoom in' }));
    expect(zoomOfTiles()).toBe(16);

    await rerender(
      <TileMap
        markers={[beach]}
        tileSource={tileSource}
        labels={labels}
        accessibilityLabel="Map of places"
        attributionUrl="https://www.openstreetmap.org/copyright"
        height={300}
        zoom={15}
        viewKey="b"
      />,
    );

    expect(zoomOfTiles()).toBe(15);
  });

  it('credits OpenStreetMap on the map, linking to the credits page', async () => {
    const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue();
    await renderMap();

    await userEvent
      .setup()
      .press(screen.getByRole('link', { name: '© OpenStreetMap contributors' }));

    expect(openURL).toHaveBeenCalledWith('https://www.openstreetmap.org/copyright');
    openURL.mockRestore();
  });
});
