import * as SecureStore from 'expo-secure-store';

import {
  clearDeliveryDetails,
  loadDeliveryDetails,
  saveDeliveryDetails,
} from './deliveryDetailsStorage';

describe('deliveryDetailsStorage', () => {
  beforeEach(async () => {
    await clearDeliveryDetails();
  });

  it('round-trips the address, phone and area for the user who saved them', async () => {
    await saveDeliveryDetails('user-1', {
      address: '12 Rue Suffren',
      phone: '+919876500001',
      area: 'whiteTown',
    });

    await expect(loadDeliveryDetails('user-1')).resolves.toEqual({
      address: '12 Rue Suffren',
      phone: '+919876500001',
      area: 'whiteTown',
    });
  });

  it('never hands them to a different user signing in on the same phone', async () => {
    await saveDeliveryDetails('user-1', { address: '12 Rue Suffren', phone: '+919876500001' });

    await expect(loadDeliveryDetails('user-2')).resolves.toBeNull();
  });

  it('returns nothing when nothing is saved, and after they are cleared', async () => {
    await expect(loadDeliveryDetails('user-1')).resolves.toBeNull();

    await saveDeliveryDetails('user-1', { address: '12 Rue Suffren', phone: '+919876500001' });
    await clearDeliveryDetails();

    await expect(loadDeliveryDetails('user-1')).resolves.toBeNull();
  });

  it('ignores corrupted or incomplete data', async () => {
    await SecureStore.setItemAsync('onepondy.delivery-details.v1', '{not json');
    await expect(loadDeliveryDetails('user-1')).resolves.toBeNull();

    await SecureStore.setItemAsync(
      'onepondy.delivery-details.v1',
      JSON.stringify({ userId: 'user-1', address: 'x' }),
    );
    await expect(loadDeliveryDetails('user-1')).resolves.toBeNull();
  });
});
