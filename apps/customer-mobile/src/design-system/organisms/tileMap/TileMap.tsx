import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Image,
  Linking,
  Pressable,
  StyleSheet,
  useWindowDimensions,
  View,
  type GestureResponderEvent,
  type LayoutChangeEvent,
} from 'react-native';

import { OPIconButton } from '../../atoms/OPIconButton';
import { OPText } from '../../atoms/OPText';
import { useThemedStyles } from '../../theme/ThemeProvider';
import type { Theme } from '../../theme/theme';
import {
  clampZoom,
  fitBounds,
  project,
  TILE_SIZE,
  tileUrl,
  unproject,
  visibleTiles,
  type LatLng,
} from './mapMath';

export type { LatLng } from './mapMath';

export type MapMarkerTone = 'brand' | 'accent' | 'alert';

export interface MapMarker extends LatLng {
  id: string;
  /** Shown under the pin, and spoken as the pin's name. */
  label?: string;
  tone?: MapMarkerTone;
  /** A bigger pin with a ring, for the one the person has picked. */
  selected?: boolean;
}

export interface TileSource {
  /** For example `https://tile.openstreetmap.org/{z}/{x}/{y}.png`. */
  urlTemplate: string;
  /** Sent with every tile request; public tile servers ask apps to identify themselves. */
  headers?: Record<string, string>;
  maxZoom?: number;
}

export interface TileMapLabels {
  zoomIn: string;
  zoomOut: string;
  /** The credit shown on the map, for example "© OpenStreetMap contributors". */
  attribution: string;
  attributionHint: string;
}

interface TileMapProps {
  markers: readonly MapMarker[];
  tileSource: TileSource;
  labels: TileMapLabels;
  /** Spoken name of the whole map, which should say what it shows. */
  accessibilityLabel: string;
  /** Where the credit links to. */
  attributionUrl: string;
  height?: number;
  /** Drag, pinch and zoom buttons. Without it the map is a picture with pins, safe to put inside a scrolling page. */
  interactive?: boolean;
  /** Changing this re-fits the view to the markers (or to `center`), for example when the list of pins changes. */
  viewKey?: string;
  /** Start here instead of fitting every marker. */
  center?: LatLng;
  zoom?: number;
  onMarkerPress?: (id: string) => void;
}

const MIN_ZOOM = 3;
const SPARE_TILES = 1;

/**
 * A map drawn from picture tiles, with pins. It needs no native map library: each tile is an ordinary image from the tile server, laid
 * out by the maths in `mapMath`. One finger drags, two fingers pinch (the zoom settles on a whole level when the fingers lift), and
 * the plus and minus buttons do the same for anyone who cannot pinch.
 */
export function TileMap({
  markers,
  tileSource,
  labels,
  accessibilityLabel,
  attributionUrl,
  height = 280,
  interactive = true,
  viewKey,
  center,
  zoom,
  onMarkerPress,
}: TileMapProps) {
  const styles = useThemedStyles(createStyles);
  const window = useWindowDimensions();
  const maxZoom = tileSource.maxZoom ?? 18;
  const [width, setWidth] = useState(window.width);

  const initial = useMemo(() => {
    if (center) {
      return { center, zoom: clampZoom(zoom ?? 15, MIN_ZOOM, maxZoom) };
    }
    const fit = fitBounds(markers, width, height, {
      minZoom: MIN_ZOOM,
      maxZoom,
      singleZoom: zoom ?? 15,
    });
    return fit;
    // The view is set again only when the caller says so (viewKey) or the map is measured; moving a pin must not undo a drag.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewKey, width, height]);

  const [view, setView] = useState(initial);
  const [pinchScale, setPinchScale] = useState(1);

  // A new starting view (the pins changed, or the map was measured) replaces whatever the person had dragged to.
  const [appliedInitial, setAppliedInitial] = useState(initial);
  if (appliedInitial !== initial) {
    setAppliedInitial(initial);
    setView(initial);
    setPinchScale(1);
  }

  // What the touch handlers read when a touch arrives, so they always see the current view without being rebuilt on every change.
  const latest = useRef({ view, maxZoom });
  useEffect(() => {
    latest.current = { view, maxZoom };
  });
  const gesture = useRef({
    startTouch: { x: 0, y: 0 },
    startPoint: { x: 0, y: 0 },
    startDistance: 0,
    pinching: false,
    scale: 1,
  });

  const onGrant = (event: GestureResponderEvent) => {
    const { view: current } = latest.current;
    const touch = event.nativeEvent.touches[0] ?? event.nativeEvent;
    gesture.current = {
      startTouch: { x: touch.pageX, y: touch.pageY },
      startPoint: project(current.center, current.zoom),
      startDistance: 0,
      pinching: false,
      scale: 1,
    };
  };

  const onMove = (event: GestureResponderEvent) => {
    const touches = event.nativeEvent.touches;
    const state = gesture.current;
    if (touches.length >= 2) {
      const distance = Math.hypot(
        touches[0].pageX - touches[1].pageX,
        touches[0].pageY - touches[1].pageY,
      );
      if (!state.pinching) {
        state.pinching = true;
        state.startDistance = Math.max(distance, 1);
      }
      state.scale = Math.max(0.25, Math.min(4, distance / state.startDistance));
      setPinchScale(state.scale);
      return;
    }
    if (state.pinching || touches.length === 0) {
      return;
    }
    const { view: current } = latest.current;
    setView({
      center: unproject(
        {
          x: state.startPoint.x - (touches[0].pageX - state.startTouch.x),
          y: state.startPoint.y - (touches[0].pageY - state.startTouch.y),
        },
        current.zoom,
      ),
      zoom: current.zoom,
    });
  };

  // Lifting the fingers settles a pinch on a whole zoom level.
  const onSettle = () => {
    const state = gesture.current;
    if (state.pinching) {
      const { view: current, maxZoom: max } = latest.current;
      const zoomed = clampZoom(current.zoom + Math.round(Math.log2(state.scale)), MIN_ZOOM, max);
      setView({ center: current.center, zoom: zoomed });
    }
    state.pinching = false;
    state.startDistance = 0;
    state.scale = 1;
    setPinchScale(1);
  };

  const tiles = useMemo(
    () => visibleTiles(view.center, view.zoom, width, height, SPARE_TILES),
    [view.center, view.zoom, width, height],
  );
  const origin = useMemo(() => {
    const middle = project(view.center, view.zoom);
    return { x: middle.x - width / 2, y: middle.y - height / 2 };
  }, [view.center, view.zoom, width, height]);

  const zoomBy = (delta: number) =>
    setView((current) => ({
      center: current.center,
      zoom: clampZoom(current.zoom + delta, MIN_ZOOM, maxZoom),
    }));

  const onLayout = (event: LayoutChangeEvent) => {
    const measured = Math.round(event.nativeEvent.layout.width);
    if (measured > 0 && measured !== width) {
      setWidth(measured);
    }
  };

  return (
    <View
      style={[styles.map, { height }]}
      onLayout={onLayout}
      accessibilityLabel={accessibilityLabel}
      accessibilityRole="image"
      testID="tile-map"
      onStartShouldSetResponder={() => interactive}
      // Once the map has the touch, a scrolling page around it must not take it back.
      onResponderTerminationRequest={() => false}
      onResponderGrant={onGrant}
      onResponderMove={onMove}
      onResponderRelease={onSettle}
      onResponderTerminate={onSettle}
    >
      <View
        pointerEvents="none"
        style={[styles.fill, { transform: [{ scale: pinchScale }] }]}
        importantForAccessibility="no-hide-descendants"
        accessibilityElementsHidden
      >
        {tiles.map((tile) => (
          <Image
            key={tile.key}
            source={{
              uri: tileUrl(tileSource.urlTemplate, tile.z, tile.x, tile.y),
              headers: tileSource.headers,
            }}
            style={[styles.tile, { left: tile.left, top: tile.top }]}
            testID="map-tile"
          />
        ))}
      </View>

      {markers.map((marker) => {
        const at = project(marker, view.zoom);
        const left = at.x - origin.x;
        const top = at.y - origin.y;
        if (left < -40 || left > width + 40 || top < -40 || top > height + 40) {
          return null;
        }
        return (
          <Pressable
            key={marker.id}
            accessibilityRole="button"
            accessibilityLabel={marker.label ?? marker.id}
            disabled={!onMarkerPress}
            onPress={() => onMarkerPress?.(marker.id)}
            hitSlop={8}
            style={[styles.markerBox, { left: left - MARKER_BOX / 2, top: top - MARKER_BOX / 2 }]}
            testID={`map-marker-${marker.id}`}
          >
            <View
              style={[
                styles.pin,
                marker.tone === 'accent'
                  ? styles.pinAccent
                  : marker.tone === 'alert'
                    ? styles.pinAlert
                    : styles.pinBrand,
                marker.selected ? styles.pinSelected : null,
              ]}
            />
            {marker.label ? (
              <View style={styles.pinLabel}>
                <OPText variant="caption" numberOfLines={1} style={styles.pinLabelText}>
                  {marker.label}
                </OPText>
              </View>
            ) : null}
          </Pressable>
        );
      })}

      {interactive ? (
        <View style={styles.zoomButtons}>
          <OPIconButton
            icon="plus"
            size="sm"
            variant="tonal"
            accessibilityLabel={labels.zoomIn}
            disabled={view.zoom >= maxZoom}
            onPress={() => zoomBy(1)}
          />
          <OPIconButton
            icon="minus"
            size="sm"
            variant="tonal"
            accessibilityLabel={labels.zoomOut}
            disabled={view.zoom <= MIN_ZOOM}
            onPress={() => zoomBy(-1)}
          />
        </View>
      ) : null}

      <Pressable
        accessibilityRole="link"
        accessibilityLabel={labels.attribution}
        accessibilityHint={labels.attributionHint}
        onPress={() => void Linking.openURL(attributionUrl)}
        hitSlop={8}
        style={styles.attribution}
      >
        <OPText variant="caption" style={styles.attributionText}>
          {labels.attribution}
        </OPText>
      </Pressable>
    </View>
  );
}

const MARKER_BOX = 44;

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    map: {
      width: '100%',
      overflow: 'hidden',
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surfaceMuted,
    },
    fill: {
      position: 'absolute',
      top: 0,
      right: 0,
      bottom: 0,
      left: 0,
    },
    tile: {
      position: 'absolute',
      width: TILE_SIZE,
      height: TILE_SIZE,
    },
    markerBox: {
      position: 'absolute',
      width: MARKER_BOX,
      height: MARKER_BOX,
      alignItems: 'center',
      justifyContent: 'center',
    },
    pin: {
      width: 22,
      height: 22,
      borderRadius: 11,
      borderWidth: 3,
      borderColor: '#FFFFFF',
    },
    pinBrand: { backgroundColor: theme.colors.brand.primary },
    pinAccent: { backgroundColor: theme.colors.brand.secondary },
    pinAlert: { backgroundColor: theme.colors.error },
    pinSelected: {
      width: 30,
      height: 30,
      borderRadius: 15,
      borderWidth: 4,
    },
    pinLabel: {
      position: 'absolute',
      top: MARKER_BOX - 4,
      maxWidth: 160,
      paddingHorizontal: theme.spacing.xs,
      borderRadius: theme.radii.sm,
      backgroundColor: 'rgba(255,255,255,0.92)',
    },
    pinLabelText: {
      color: '#1C2421',
    },
    zoomButtons: {
      position: 'absolute',
      top: theme.spacing.xs,
      right: theme.spacing.xs,
      gap: theme.spacing.xxs,
    },
    attribution: {
      position: 'absolute',
      right: 0,
      bottom: 0,
      paddingHorizontal: theme.spacing.xs,
      paddingVertical: 2,
      borderTopLeftRadius: theme.radii.sm,
      backgroundColor: 'rgba(255,255,255,0.85)',
    },
    attributionText: {
      color: '#1C2421',
      fontSize: 11,
      lineHeight: 14,
    },
  });
