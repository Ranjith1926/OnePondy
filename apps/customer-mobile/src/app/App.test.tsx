import AsyncStorage from '@react-native-async-storage/async-storage';
import { render, screen, userEvent, waitFor } from '@testing-library/react-native';

import App from './App';

const STORAGE_KEY = 'onepondy.preferences.v1';

/** React Navigation names tab buttons "<Label>, tab, <n> of <total>". */
const tab = (label: string) => screen.getByRole('button', { name: new RegExp(`^${label}, tab`) });

async function launchOnboarded() {
  await AsyncStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({ language: 'en', theme: 'system', onboardingCompleted: true }),
  );
  await render(<App />);
  await screen.findByRole('button', { name: 'Where do you want to go?' });
}

describe('<App />', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('shows onboarding on first launch and remembers it is complete', async () => {
    const user = userEvent.setup();
    await render(<App />);

    expect(await screen.findByRole('header', { name: 'Discover Puducherry' })).toBeOnTheScreen();
    expect(screen.getByLabelText('Step 1 of 3')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByRole('header', { name: 'Plan your days' })).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Next' }));
    await user.press(screen.getByRole('button', { name: 'Get started' }));

    expect(
      await screen.findByRole('button', { name: 'Where do you want to go?' }),
    ).toBeOnTheScreen();
    await waitFor(async () => {
      expect(JSON.parse((await AsyncStorage.getItem(STORAGE_KEY)) ?? '{}')).toMatchObject({
        onboardingCompleted: true,
      });
    });
  });

  it('skips onboarding for returning users and shows the five tabs', async () => {
    await launchOnboarded();

    for (const label of ['Home', 'Explore', 'Bookings', 'Saved', 'Profile']) {
      expect(tab(label)).toBeOnTheScreen();
    }
    expect(tab('Home')).toBeSelected();
  });

  it('explains services that are not available yet', async () => {
    const user = userEvent.setup();
    await launchOnboarded();

    await user.press(screen.getByRole('button', { name: 'Food, Soon' }));

    expect(await screen.findByRole('header', { name: 'Food is coming soon' })).toBeOnTheScreen();
    expect(
      screen.getByText('We never show a service as available before it is.'),
    ).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Got it' }));
    await waitFor(() => expect(screen.queryByText('Food is coming soon')).not.toBeOnTheScreen());
  });

  it('searches sample places and shows a no-results state', async () => {
    const user = userEvent.setup();
    await launchOnboarded();

    await user.press(screen.getByRole('button', { name: 'Where do you want to go?' }));
    const field = await screen.findByLabelText('Search places and areas');

    await user.type(field, 'temple');
    expect(await screen.findByText('Manakula Vinayagar Temple')).toBeOnTheScreen();
    expect(screen.getByText('1 place')).toBeOnTheScreen();

    await user.clear(field);
    await user.type(field, 'zzz');
    expect(await screen.findByRole('header', { name: 'No matches for “zzz”' })).toBeOnTheScreen();
  });

  it('changes the selected area from the location header', async () => {
    const user = userEvent.setup();
    await launchOnboarded();

    await user.press(screen.getByRole('button', { name: 'Your area: Puducherry' }));
    await user.press(await screen.findByRole('radio', { name: 'Auroville' }));

    expect(await screen.findByRole('button', { name: 'Your area: Auroville' })).toBeOnTheScreen();
  });

  it('filters Explore by category', async () => {
    const user = userEvent.setup();
    await launchOnboarded();

    await user.press(tab('Explore'));
    await user.press(await screen.findByRole('button', { name: 'Museums' }));

    expect(screen.getByRole('button', { name: 'Museums' })).toBeSelected();
    expect(screen.getByText('Pondicherry Museum')).toBeOnTheScreen();
    expect(screen.queryByText('Promenade Beach')).not.toBeOnTheScreen();
  });

  it('switches to dark appearance from Profile and saves it', async () => {
    const user = userEvent.setup();
    await launchOnboarded();

    await user.press(tab('Profile'));
    await user.press(await screen.findByRole('button', { name: 'Appearance, Use device setting' }));
    await user.press(await screen.findByRole('radio', { name: 'Dark' }));

    expect(await screen.findByRole('button', { name: 'Appearance, Dark' })).toBeOnTheScreen();
    await waitFor(async () => {
      expect(JSON.parse((await AsyncStorage.getItem(STORAGE_KEY)) ?? '{}')).toMatchObject({
        theme: 'dark',
      });
    });
  });

  it('lists Tamil and French as unavailable until translations are reviewed', async () => {
    const user = userEvent.setup();
    await launchOnboarded();

    await user.press(tab('Profile'));
    await user.press(await screen.findByRole('button', { name: 'Language, English' }));

    expect(await screen.findByRole('radio', { name: 'English' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'தமிழ், Translation in progress' })).toBeDisabled();
    expect(screen.getByRole('radio', { name: 'Français, Translation in progress' })).toBeDisabled();
  });
});
