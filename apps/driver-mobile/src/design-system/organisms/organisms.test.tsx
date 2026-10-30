import { screen, userEvent } from '@testing-library/react-native';
import { Text } from 'react-native';

import { renderWithProviders } from '../../test-utils/render';
import { ModalSheet } from './ModalSheet';

describe('<ModalSheet />', () => {
  it('shows its title and content when visible', async () => {
    await renderWithProviders(
      <ModalSheet visible title="Food is coming soon" closeLabel="Close" onClose={() => {}}>
        <Text>Details</Text>
      </ModalSheet>,
    );

    expect(screen.getByRole('header', { name: 'Food is coming soon' })).toBeOnTheScreen();
    expect(screen.getByText('Details')).toBeOnTheScreen();
  });

  it('closes from the close button and the backdrop', async () => {
    const onClose = jest.fn();
    const user = userEvent.setup();
    await renderWithProviders(
      <ModalSheet visible title="Appearance" closeLabel="Close" onClose={onClose}>
        <Text>Options</Text>
      </ModalSheet>,
    );

    await user.press(screen.getByRole('button', { name: 'Close' }));
    // The backdrop is hidden from screen readers (the sheet is the modal); sighted users can tap it.
    await user.press(screen.getByTestId('modal-sheet-backdrop', { includeHiddenElements: true }));

    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('renders nothing when hidden', async () => {
    await renderWithProviders(
      <ModalSheet visible={false} title="Hidden" closeLabel="Close" onClose={() => {}}>
        <Text>Not shown</Text>
      </ModalSheet>,
    );

    expect(screen.queryByText('Not shown')).not.toBeOnTheScreen();
  });
});
