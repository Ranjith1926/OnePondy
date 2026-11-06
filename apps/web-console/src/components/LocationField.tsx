'use client';

import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';

import { formatCoordinate, parseCoordinate, startingPoint } from '@/lib/coordinates';
import { clampZoom, project, tileUrl, unproject, visibleTiles, type LatLng } from '@/lib/mapMath';

import { Button } from './Button';
import { TextField } from './Field';

/** Where the map pictures come from. OpenStreetMap's public servers are for light use; point this at another provider before a busy launch. */
const tileTemplate =
  process.env.NEXT_PUBLIC_MAP_TILE_URL ?? 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';

/** Puducherry, shown until a position is chosen. */
const town: LatLng = { latitude: 11.9416, longitude: 79.8083 };
const MAP_HEIGHT = 288;
const MIN_ZOOM = 3;
const MAX_ZOOM = 18;
const DRAG_SLOP_PX = 4;

interface LocationFieldProps {
  /** Field names, and the ids of the two text boxes. Forms differ in what they call them. */
  latitudeName?: string;
  longitudeName?: string;
  defaultLatitude?: number | null;
  defaultLongitude?: number | null;
  latitudeErrors?: string[];
  longitudeErrors?: string[];
  hint?: string;
}

interface View {
  center: LatLng;
  zoom: number;
}

/**
 * Latitude and longitude for a form, with a map to set them by clicking where the place is, a button that uses the position of the
 * device, and the two boxes to type them. The boxes are the real fields (they are what gets sent); the map only fills them in.
 * Nothing is looked up from an address, and nothing about the position is sent anywhere but the form's own submit.
 */
export function LocationField({
  latitudeName = 'lat',
  longitudeName = 'lng',
  defaultLatitude = null,
  defaultLongitude = null,
  latitudeErrors,
  longitudeErrors,
  hint,
}: LocationFieldProps) {
  const [latitudeText, setLatitudeText] = useState(
    defaultLatitude === null ? '' : String(defaultLatitude),
  );
  const [longitudeText, setLongitudeText] = useState(
    defaultLongitude === null ? '' : String(defaultLongitude),
  );
  const [view, setView] = useState<View>(() => {
    const start = startingPoint(latitudeText, longitudeText, town);
    return { center: start, zoom: start.hasPin ? 16 : 13 };
  });
  const [width, setWidth] = useState(640);
  const [locating, setLocating] = useState(false);
  const [locationProblem, setLocationProblem] = useState<string | null>(null);
  const [canLocate, setCanLocate] = useState(false);
  const mapRef = useRef<HTMLDivElement>(null);
  const drag = useRef<{
    x: number;
    y: number;
    start: { x: number; y: number };
    moved: boolean;
  } | null>(null);

  const pin = startingPoint(latitudeText, longitudeText, town);

  useEffect(() => {
    // Only a browser can say whether the device can tell us where it is.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setCanLocate(typeof navigator !== 'undefined' && 'geolocation' in navigator);
  }, []);

  useEffect(() => {
    const element = mapRef.current;
    if (!element || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(() => {
      const measured = Math.round(element.getBoundingClientRect().width);
      if (measured > 0) {
        setWidth(measured);
      }
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const origin = (() => {
    const middle = project(view.center, view.zoom);
    return { x: middle.x - width / 2, y: middle.y - MAP_HEIGHT / 2 };
  })();
  const tiles = visibleTiles(view.center, view.zoom, width, MAP_HEIGHT, 1);
  const pinAt = pin.hasPin ? project(pin, view.zoom) : null;

  const choose = (position: LatLng) => {
    setLatitudeText(formatCoordinate(position.latitude));
    setLongitudeText(formatCoordinate(position.longitude));
  };

  const zoomBy = (delta: number) =>
    setView((current) => ({
      ...current,
      zoom: clampZoom(current.zoom + delta, MIN_ZOOM, MAX_ZOOM),
    }));

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    event.currentTarget.setPointerCapture?.(event.pointerId);
    drag.current = {
      x: event.clientX,
      y: event.clientY,
      start: project(view.center, view.zoom),
      moved: false,
    };
  };

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    if (!state) {
      return;
    }
    const dx = event.clientX - state.x;
    const dy = event.clientY - state.y;
    if (!state.moved && Math.hypot(dx, dy) < DRAG_SLOP_PX) {
      return;
    }
    state.moved = true;
    setView((current) => ({
      ...current,
      center: unproject({ x: state.start.x - dx, y: state.start.y - dy }, current.zoom),
    }));
  };

  const onPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    drag.current = null;
    if (!state || state.moved) {
      return;
    }
    // A press that did not move is a click: that is where the place is.
    const rect = event.currentTarget.getBoundingClientRect();
    choose(
      unproject(
        { x: origin.x + (event.clientX - rect.left), y: origin.y + (event.clientY - rect.top) },
        view.zoom,
      ),
    );
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const step = 80;
    const moves: Record<string, [number, number]> = {
      ArrowLeft: [-step, 0],
      ArrowRight: [step, 0],
      ArrowUp: [0, -step],
      ArrowDown: [0, step],
    };
    const move = moves[event.key];
    if (move) {
      event.preventDefault();
      setView((current) => {
        const at = project(current.center, current.zoom);
        return {
          ...current,
          center: unproject({ x: at.x + move[0], y: at.y + move[1] }, current.zoom),
        };
      });
    } else if (event.key === '+' || event.key === '=') {
      zoomBy(1);
    } else if (event.key === '-') {
      zoomBy(-1);
    } else if (event.key === 'Enter') {
      // The keyboard way to place the pin: wherever the middle of the map is.
      event.preventDefault();
      choose(view.center);
    }
  };

  // Typing a position moves the map to it; clicking on the map does not move the map under the pointer.
  const typed = (kind: 'latitude' | 'longitude', text: string) => {
    const nextLatitude = kind === 'latitude' ? text : latitudeText;
    const nextLongitude = kind === 'longitude' ? text : longitudeText;
    if (kind === 'latitude') {
      setLatitudeText(text);
    } else {
      setLongitudeText(text);
    }
    const target = startingPoint(nextLatitude, nextLongitude, town);
    if (target.hasPin) {
      setView({ center: target, zoom: Math.max(view.zoom, 15) });
    }
  };

  const useMyLocation = () => {
    setLocating(true);
    setLocationProblem(null);
    navigator.geolocation.getCurrentPosition(
      (result) => {
        const here = { latitude: result.coords.latitude, longitude: result.coords.longitude };
        choose(here);
        setView({ center: here, zoom: 17 });
        setLocating(false);
      },
      () => {
        setLocationProblem(
          "This device didn't share its position. Allow location for this site, or click the map instead.",
        );
        setLocating(false);
      },
      { enableHighAccuracy: true, timeout: 15_000 },
    );
  };

  const clear = () => {
    setLatitudeText('');
    setLongitudeText('');
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          id={latitudeName}
          label="Latitude"
          inputMode="decimal"
          hint={
            hint ??
            'Optional; both latitude and longitude, or neither. Click the map to fill them in.'
          }
          errors={latitudeErrors}
          value={latitudeText}
          onChange={(event) => typed('latitude', event.target.value)}
        />
        <TextField
          id={longitudeName}
          label="Longitude"
          inputMode="decimal"
          errors={longitudeErrors}
          value={longitudeText}
          onChange={(event) => typed('longitude', event.target.value)}
        />
      </div>

      <div
        ref={mapRef}
        role="application"
        aria-label="Map for choosing the position"
        aria-describedby="location-map-help"
        tabIndex={0}
        data-testid="location-map"
        className="relative w-full touch-none select-none overflow-hidden rounded-lg border border-line-strong bg-canvas focus-visible:outline-2 focus-visible:outline-brand"
        style={{ height: MAP_HEIGHT, cursor: 'crosshair' }}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={() => {
          drag.current = null;
        }}
        onKeyDown={onKeyDown}
      >
        {tiles.map((tile) => (
          // Map pictures come straight from the tile server; Next's image optimiser has no part in this.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            key={tile.key}
            src={tileUrl(tileTemplate, tile.z, tile.x, tile.y)}
            alt=""
            draggable={false}
            width={256}
            height={256}
            className="pointer-events-none absolute max-w-none"
            style={{ left: tile.left, top: tile.top }}
          />
        ))}
        {pinAt ? (
          <span
            data-testid="location-pin"
            aria-hidden="true"
            className="pointer-events-none absolute h-5 w-5 rounded-full border-[3px] border-white bg-brand shadow"
            style={{ left: pinAt.x - origin.x - 10, top: pinAt.y - origin.y - 10 }}
          />
        ) : null}
        <div className="absolute right-2 top-2 flex flex-col gap-1">
          <button
            type="button"
            aria-label="Zoom in"
            onPointerDown={(event) => event.stopPropagation()}
            onPointerUp={(event) => event.stopPropagation()}
            onClick={() => zoomBy(1)}
            className="h-9 w-9 rounded-lg border border-line-strong bg-surface text-lg font-semibold shadow"
          >
            +
          </button>
          <button
            type="button"
            aria-label="Zoom out"
            onPointerDown={(event) => event.stopPropagation()}
            onPointerUp={(event) => event.stopPropagation()}
            onClick={() => zoomBy(-1)}
            className="h-9 w-9 rounded-lg border border-line-strong bg-surface text-lg font-semibold shadow"
          >
            −
          </button>
        </div>
        <a
          href="https://www.openstreetmap.org/copyright"
          target="_blank"
          rel="noreferrer"
          onPointerDown={(event) => event.stopPropagation()}
          onPointerUp={(event) => event.stopPropagation()}
          className="absolute bottom-0 right-0 bg-white/90 px-2 py-0.5 text-xs text-ink underline"
        >
          © OpenStreetMap contributors
        </a>
      </div>

      <p id="location-map-help" className="text-xs text-muted">
        Click the map where the place is, or drag to move it. With the keyboard: arrow keys move the
        map, + and − zoom, Enter puts the pin at the middle. Only the map pictures are fetched from
        OpenStreetMap; the position you choose is not sent to it.
      </p>

      <div className="flex flex-wrap items-center gap-2">
        {canLocate ? (
          <Button variant="secondary" onClick={useMyLocation} disabled={locating}>
            {locating ? 'Finding your position…' : 'Use my current location'}
          </Button>
        ) : null}
        {pin.hasPin ? (
          <Button variant="ghost" onClick={clear}>
            Clear position
          </Button>
        ) : null}
      </div>
      {locationProblem ? (
        <p role="alert" className="text-xs font-medium text-danger">
          {locationProblem}
        </p>
      ) : null}
      {parseCoordinate(latitudeText, 'latitude') === null && latitudeText.trim() !== '' ? (
        <p className="text-xs text-muted">The latitude is not a number between −90 and 90.</p>
      ) : null}
    </div>
  );
}
