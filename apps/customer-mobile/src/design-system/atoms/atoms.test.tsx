import { screen, userEvent } from '@testing-library/react-native';
import { useState } from 'react';

import { renderWithProviders } from '../../test-utils/render';
import { OPBadge } from './OPBadge';
import { OPChip } from './OPChip';
import { OPIconButton } from './OPIconButton';
import { OPImage } from './OPImage';
import { OPSearchField } from './OPSearchField';

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

describe('<OPSearchField />', () => {
  function ControlledSearch() {
    const [value, setValue] = useState('');
    return (
      <OPSearchField
        value={value}
        onChangeText={setValue}
        placeholder="Search places"
        clearLabel="Clear search"
      />
    );
  }

  it('shows a clear button only while there is text, and clears it', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ControlledSearch />);

    expect(screen.queryByRole('button', { name: 'Clear search' })).not.toBeOnTheScreen();

    await user.type(screen.getByLabelText('Search places'), 'beach');
    expect(screen.getByDisplayValue('beach')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Clear search' }));
    expect(screen.getByLabelText('Search places')).toHaveDisplayValue('');
    expect(screen.queryByRole('button', { name: 'Clear search' })).not.toBeOnTheScreen();
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

describe('<OPImage />', () => {
  it('shows the fallback tile when there is no image', async () => {
    await renderWithProviders(<OPImage fallbackIcon="waves" aspectRatio={1} />);

    expect(screen.getByTestId('image-fallback')).toBeOnTheScreen();
  });

  it('is announced as an image only when it has a label', async () => {
    await renderWithProviders(
      <OPImage fallbackIcon="waves" aspectRatio={1} accessibilityLabel="Promenade Beach at dawn" />,
    );

    expect(screen.getByRole('image', { name: 'Promenade Beach at dawn' })).toBeOnTheScreen();
  });
});
