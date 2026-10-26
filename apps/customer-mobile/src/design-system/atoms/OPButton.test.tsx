import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../test-utils/render';
import { OPButton } from './OPButton';

describe('<OPButton />', () => {
  it('is an accessible button that calls onPress', async () => {
    const onPress = jest.fn();
    await renderWithProviders(<OPButton label="Explore places" onPress={onPress} />);

    await userEvent.setup().press(screen.getByRole('button', { name: 'Explore places' }));

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('does not respond when disabled and reports the disabled state', async () => {
    const onPress = jest.fn();
    await renderWithProviders(<OPButton label="Book now" onPress={onPress} disabled />);

    const button = screen.getByRole('button', { name: 'Book now' });
    await userEvent.setup().press(button);

    expect(button).toBeDisabled();
    expect(onPress).not.toHaveBeenCalled();
  });

  it('reports busy and ignores presses while loading, keeping its label', async () => {
    const onPress = jest.fn();
    await renderWithProviders(<OPButton label="Saving" onPress={onPress} loading />);

    const button = screen.getByRole('button', { name: 'Saving' });
    await userEvent.setup().press(button);

    expect(button).toBeBusy();
    expect(button).toBeDisabled();
    expect(onPress).not.toHaveBeenCalled();
  });

  it('meets the 48dp minimum touch height at the default size', async () => {
    await renderWithProviders(<OPButton label="Next" onPress={() => {}} />);

    expect(screen.getByRole('button', { name: 'Next' })).toHaveStyle({ minHeight: 48 });
  });
});
