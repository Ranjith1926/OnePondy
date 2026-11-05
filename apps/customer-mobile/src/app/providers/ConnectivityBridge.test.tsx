import { onlineManager } from '@tanstack/react-query';
import { act, screen } from '@testing-library/react-native';

import { LoadingState } from '../../design-system';
import { renderWithProviders } from '../../test-utils/render';
import { ConnectivityBridge } from './ConnectivityBridge';

afterEach(() => {
  onlineManager.setOnline(true);
});

describe('<ConnectivityBridge />', () => {
  it('lets a loading screen say it is offline, in English, and stops saying it when the phone is back', async () => {
    await renderWithProviders(
      <ConnectivityBridge>
        <LoadingState accessibilityLabel="Loading this place" />
      </ConnectivityBridge>,
    );
    expect(screen.queryByText(/You're offline/)).not.toBeOnTheScreen();

    await act(async () => {
      onlineManager.setOnline(false);
    });
    expect(
      screen.getByText("You're offline. This will load when you're back online."),
    ).toBeOnTheScreen();

    await act(async () => {
      onlineManager.setOnline(true);
    });
    expect(screen.queryByText(/You're offline/)).not.toBeOnTheScreen();
  });
});
