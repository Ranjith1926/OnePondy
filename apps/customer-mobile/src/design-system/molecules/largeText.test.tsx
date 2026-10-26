import { screen } from '@testing-library/react-native';

import { renderWithProviders } from '../../test-utils/render';
import { ListItem } from './ListItem';
import { PlaceCard } from './PlaceCard';
import { ServiceShortcut } from './ServiceShortcut';

// Simulates a large system font (spec §4: scalable text) without changing real device settings.
jest.mock('../hooks/useLargeText', () => ({ useLargeText: () => true }));

describe('layouts with large system text', () => {
  it('stack a list value under its title while keeping the spoken label', async () => {
    await renderWithProviders(
      <ListItem
        title="Appearance"
        trailing={{ kind: 'value', text: 'Use device setting' }}
        onPress={() => {}}
      />,
    );

    expect(
      screen.getByRole('button', { name: 'Appearance, Use device setting' }),
    ).toBeOnTheScreen();
    expect(screen.getByText('Use device setting')).toBeOnTheScreen();
  });

  it('keep a service status visible and announced', async () => {
    await renderWithProviders(
      <ServiceShortcut label="Activities" icon="ticket" badgeLabel="Soon" onPress={() => {}} />,
    );

    expect(screen.getByRole('button', { name: 'Activities, Soon' })).toBeOnTheScreen();
    expect(screen.getByText('Soon')).toBeOnTheScreen();
  });

  it('keep the sample badge on list cards', async () => {
    await renderWithProviders(
      <PlaceCard
        title="Ousteri Lake"
        subtitle="Nature · Villianur"
        fallbackIcon="trees"
        badgeLabel="Sample"
        variant="list"
      />,
    );

    expect(screen.getByText('Sample')).toBeOnTheScreen();
  });
});
