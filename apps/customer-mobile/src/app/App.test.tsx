import AsyncStorage from '@react-native-async-storage/async-storage';
import { render, screen, userEvent, waitFor } from '@testing-library/react-native';

import App from './App';

const STORAGE_KEY = 'onepondy.preferences.v1';

/** React Navigation names tab buttons "<Label>, tab, <n> of <total>". */
const tab = (label: string) => screen.getByRole('button', { name: new RegExp(`^${label}, tab`) });

interface FixturePlace {
  id: string;
  slug: string;
  name: string;
  categoryCode: string;
}

// Verified-looking fixture content standing in for the real API (spec §10, Phase 4).
const placeFixtures: FixturePlace[] = [
  { id: 'place-1', slug: 'promenade-beach', name: 'Promenade Beach', categoryCode: 'beach' },
  {
    id: 'place-2',
    slug: 'manakula-vinayagar-temple',
    name: 'Manakula Vinayagar Temple',
    categoryCode: 'spiritual',
  },
  { id: 'place-3', slug: 'pondicherry-museum', name: 'Pondicherry Museum', categoryCode: 'museum' },
];

function installFakePlacesApi() {
  globalThis.fetch = jest.fn((input: string) => {
    const url = new URL(input);
    if (url.pathname === '/api/v1/places') {
      const category = url.searchParams.get('category');
      const q = url.searchParams.get('q')?.toLocaleLowerCase();
      const items = placeFixtures
        .filter((place) => !category || place.categoryCode === category)
        .filter((place) => !q || place.name.toLocaleLowerCase().includes(q))
        .map((place) => ({
          ...place,
          latitude: null,
          longitude: null,
          coverImage: null,
          distanceKm: null,
        }));
      const body = { items, page: 1, pageSize: items.length, totalCount: items.length };
      return Promise.resolve({
        ok: true,
        status: 200,
        text: () => Promise.resolve(JSON.stringify(body)),
      });
    }
    return Promise.resolve({ ok: false, status: 404, text: () => Promise.resolve('') });
  }) as unknown as typeof fetch;
}

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
    installFakePlacesApi();
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
    expect(await screen.findByText('1 place')).toBeOnTheScreen();

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
