import { screen, waitFor } from '@testing-library/react-native';
import { AccessibilityInfo, Animated } from 'react-native';

import { renderWithProviders } from '../../test-utils/render';
import { OPSkeleton } from './OPSkeleton';

describe('<OPSkeleton />', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('does not animate when the user prefers reduced motion', async () => {
    const setting = jest.spyOn(AccessibilityInfo, 'isReduceMotionEnabled').mockResolvedValue(true);
    const loop = jest.spyOn(Animated, 'loop');

    await renderWithProviders(<OPSkeleton height={20} />);

    await waitFor(() => expect(setting).toHaveBeenCalled());
    expect(loop).not.toHaveBeenCalled();
  });

  it('pulses once the setting allows motion', async () => {
    jest.spyOn(AccessibilityInfo, 'isReduceMotionEnabled').mockResolvedValue(false);
    const loop = jest.spyOn(Animated, 'loop');

    await renderWithProviders(<OPSkeleton height={20} />);

    await waitFor(() => expect(loop).toHaveBeenCalled());
  });

  it('is hidden from screen readers', async () => {
    await renderWithProviders(<OPSkeleton height={20} />);

    expect(screen.queryByTestId('skeleton')).not.toBeOnTheScreen();
    expect(screen.getByTestId('skeleton', { includeHiddenElements: true })).toBeOnTheScreen();
  });
});
