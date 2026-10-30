import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../test-utils/render';
import { EmptyState } from './EmptyState';
import { ErrorState } from './ErrorState';
import { FormField } from './FormField';
import { ListItem } from './ListItem';
import { LoadingState } from './LoadingState';

describe('<EmptyState />', () => {
  it('shows its message and runs the action', async () => {
    const onAction = jest.fn();
    await renderWithProviders(
      <EmptyState
        icon="search-x"
        title="No places here yet"
        message="Places will appear once verified."
        actionLabel="Show all places"
        onAction={onAction}
      />,
    );

    expect(screen.getByRole('header', { name: 'No places here yet' })).toBeOnTheScreen();
    expect(screen.getByText('Places will appear once verified.')).toBeOnTheScreen();

    await userEvent.setup().press(screen.getByRole('button', { name: 'Show all places' }));
    expect(onAction).toHaveBeenCalledTimes(1);
  });

  it('omits the action button when there is no action', async () => {
    await renderWithProviders(
      <EmptyState icon="heart" title="Nothing saved" message="Save places." />,
    );

    expect(screen.queryByRole('button')).not.toBeOnTheScreen();
  });
});

describe('<ErrorState />', () => {
  it('offers a retry action', async () => {
    const onRetry = jest.fn();
    await renderWithProviders(
      <ErrorState
        title="Something went wrong"
        message="Please try again."
        retryLabel="Try again"
        onRetry={onRetry}
      />,
    );

    await userEvent.setup().press(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('shows the retry button as busy while retrying', async () => {
    await renderWithProviders(
      <ErrorState
        kind="offline"
        title="You're offline"
        message="Reconnect."
        retryLabel="Try again"
        onRetry={() => {}}
        retrying
      />,
    );

    expect(screen.getByRole('button', { name: 'Try again' })).toBeBusy();
  });
});

describe('<LoadingState />', () => {
  it('is announced as one busy progress element', async () => {
    await renderWithProviders(<LoadingState accessibilityLabel="Loading places" />);

    const progress = screen.getByRole('progressbar', { name: 'Loading places' });
    expect(progress).toBeBusy();
  });
});

describe('<FormField />', () => {
  it('labels the input and announces errors', async () => {
    await renderWithProviders(<FormField label="Email" error="Enter a complete email address" />);

    expect(screen.getByLabelText('Email')).toHaveProp(
      'accessibilityHint',
      'Enter a complete email address',
    );
    expect(screen.getByText('Enter a complete email address')).toBeOnTheScreen();
  });

  it('shows the hint when there is no error', async () => {
    await renderWithProviders(<FormField label="Name" hint="As on your ID" />);

    expect(screen.getByText('As on your ID')).toBeOnTheScreen();
  });
});

describe('<ListItem />', () => {
  it('acts as a radio option with a checked state', async () => {
    const onPress = jest.fn();
    await renderWithProviders(
      <ListItem title="Dark" trailing={{ kind: 'radio', selected: true }} onPress={onPress} />,
    );

    const option = screen.getByRole('radio', { name: 'Dark' });
    expect(option).toBeChecked();

    await userEvent.setup().press(option);
    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('includes the current value in its spoken label', async () => {
    await renderWithProviders(
      <ListItem
        title="Language"
        trailing={{ kind: 'value', text: 'English' }}
        onPress={() => {}}
      />,
    );

    expect(screen.getByRole('button', { name: 'Language, English' })).toBeOnTheScreen();
  });

  it('does not respond when disabled', async () => {
    const onPress = jest.fn();
    await renderWithProviders(
      <ListItem
        title="Tamil"
        disabled
        trailing={{ kind: 'radio', selected: false }}
        onPress={onPress}
      />,
    );

    const option = screen.getByRole('radio', { name: 'Tamil' });
    await userEvent.setup().press(option);

    expect(option).toBeDisabled();
    expect(onPress).not.toHaveBeenCalled();
  });
});
