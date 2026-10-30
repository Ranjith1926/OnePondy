import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../test-utils/render';
import { OPBadge } from './OPBadge';
import { OPChip } from './OPChip';
import { OPIconButton } from './OPIconButton';

describe('<OPIconButton />', () => {
  it('exposes its label and a 48dp target', async () => {
    const onPress = jest.fn();
    await renderWithProviders(
      <OPIconButton icon="bell" accessibilityLabel="Notifications" onPress={onPress} />,
    );

    const button = screen.getByRole('button', { name: 'Notifications' });
    await userEvent.setup().press(button);

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(button).toHaveStyle({ width: 48, height: 48 });
  });
});

describe('<OPChip />', () => {
  it('reports its selected state', async () => {
    await renderWithProviders(
      <>
        <OPChip label="Beaches" selected onPress={() => {}} />
        <OPChip label="Heritage" onPress={() => {}} />
      </>,
    );

    expect(screen.getByRole('button', { name: 'Beaches' })).toBeSelected();
    expect(screen.getByRole('button', { name: 'Heritage' })).not.toBeSelected();
  });
});

describe('<OPBadge />', () => {
  it('renders its label', async () => {
    await renderWithProviders(<OPBadge label="Sample" />);

    expect(screen.getByText('Sample')).toBeOnTheScreen();
  });
});
