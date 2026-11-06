import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { LocationField } from './LocationField';

// jsdom has no pointer events with coordinates; a mouse event with a pointer id is close enough for these handlers.
if (typeof window.PointerEvent === 'undefined') {
  class TestPointerEvent extends MouseEvent {
    pointerId: number;
    constructor(type: string, init: MouseEventInit & { pointerId?: number } = {}) {
      super(type, init);
      this.pointerId = init.pointerId ?? 1;
    }
  }
  window.PointerEvent = TestPointerEvent as unknown as typeof PointerEvent;
}

/** The map is 640 pixels wide in these tests (no layout in jsdom), and 288 high, so its middle is at (320, 144). */
const middle = { clientX: 320, clientY: 144 };

describe('<LocationField />', () => {
  it('starts with no pin and the two boxes empty', () => {
    render(<LocationField />);

    expect(screen.getByLabelText('Latitude')).toHaveValue('');
    expect(screen.getByLabelText('Longitude')).toHaveValue('');
    expect(screen.queryByTestId('location-pin')).not.toBeInTheDocument();
    // Map pictures are drawn even before a position is chosen.
    expect(screen.getByTestId('location-map').querySelectorAll('img').length).toBeGreaterThan(0);
  });

  it('shows the pin and the picture credit for a saved position', () => {
    render(<LocationField defaultLatitude={11.9341} defaultLongitude={79.8306} />);

    expect(screen.getByLabelText('Latitude')).toHaveValue('11.9341');
    expect(screen.getByTestId('location-pin')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /OpenStreetMap contributors/ })).toHaveAttribute(
      'href',
      'https://www.openstreetmap.org/copyright',
    );
  });

  it('fills in the boxes where the map is clicked, and shows the pin', () => {
    render(<LocationField />);
    const map = screen.getByTestId('location-map');

    fireEvent.pointerDown(map, { pointerId: 1, ...middle });
    fireEvent.pointerUp(map, { pointerId: 1, ...middle });

    // The middle of the map starts on the town.
    expect(Number(screen.getByLabelText<HTMLInputElement>('Latitude').value)).toBeCloseTo(
      11.9416,
      3,
    );
    expect(Number(screen.getByLabelText<HTMLInputElement>('Longitude').value)).toBeCloseTo(
      79.8083,
      3,
    );
    expect(screen.getByTestId('location-pin')).toBeInTheDocument();
  });

  it('treats a drag as moving the map, not as choosing a place', () => {
    render(<LocationField />);
    const map = screen.getByTestId('location-map');

    fireEvent.pointerDown(map, { pointerId: 1, ...middle });
    fireEvent.pointerMove(map, { pointerId: 1, clientX: 380, clientY: 190 });
    fireEvent.pointerUp(map, { pointerId: 1, clientX: 380, clientY: 190 });

    expect(screen.getByLabelText('Latitude')).toHaveValue('');
    expect(screen.queryByTestId('location-pin')).not.toBeInTheDocument();
  });

  it('puts the pin at the middle of the map with the Enter key', () => {
    render(<LocationField />);

    fireEvent.keyDown(screen.getByTestId('location-map'), { key: 'Enter' });

    expect(Number(screen.getByLabelText<HTMLInputElement>('Latitude').value)).toBeCloseTo(
      11.9416,
      3,
    );
  });

  it('moves the pin to what is typed, and clears it', async () => {
    const user = userEvent.setup();
    render(<LocationField />);

    await user.type(screen.getByLabelText('Latitude'), '11.93');
    expect(screen.queryByTestId('location-pin')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Longitude'), '79.83');
    expect(screen.getByTestId('location-pin')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear position' }));

    expect(screen.getByLabelText('Latitude')).toHaveValue('');
    expect(screen.getByLabelText('Longitude')).toHaveValue('');
    expect(screen.queryByTestId('location-pin')).not.toBeInTheDocument();
  });

  it('uses the device position when asked, and says so when it is refused', async () => {
    const user = userEvent.setup();
    const getCurrentPosition = vi
      .fn()
      .mockImplementationOnce((ok: (position: unknown) => void) =>
        ok({ coords: { latitude: 11.95, longitude: 79.81 } }),
      )
      .mockImplementationOnce((_ok: unknown, fail: () => void) => fail());
    Object.defineProperty(navigator, 'geolocation', {
      value: { getCurrentPosition },
      configurable: true,
    });
    render(<LocationField />);

    await user.click(await screen.findByRole('button', { name: 'Use my current location' }));
    expect(screen.getByLabelText('Latitude')).toHaveValue('11.950000');
    expect(screen.getByLabelText('Longitude')).toHaveValue('79.810000');

    await user.click(screen.getByRole('button', { name: 'Use my current location' }));
    expect(await screen.findByRole('alert')).toHaveTextContent("didn't share its position");
  });

  it('submits the boxes with the names the form expects', () => {
    const { container } = render(
      <form>
        <LocationField
          latitudeName="latitude"
          longitudeName="longitude"
          defaultLatitude={1}
          defaultLongitude={2}
        />
      </form>,
    );

    const data = new FormData(container.querySelector('form')!);
    expect(data.get('latitude')).toBe('1');
    expect(data.get('longitude')).toBe('2');
  });
});
