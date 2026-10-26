import { render, screen } from '@testing-library/react-native';

import App from './App';

describe('<App />', () => {
  it('renders the app name as an accessible header', async () => {
    await render(<App />);

    expect(screen.getByRole('header', { name: 'OnePondy' })).toBeOnTheScreen();
  });
});
