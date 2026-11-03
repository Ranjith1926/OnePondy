import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useEffect, useState } from 'react';

import { getApiAccessToken } from '../../../api/apiClient';
import { apiBaseUrl } from '../../../api/config';

const retryMs = 30_000;
const reconnectDelays = [0, 2_000, 5_000, 10_000];

/**
 * Live updates for the driver's rides (spec Phase 9: SignalR updates, reconnect behaviour and API refetch). The hub says only "a ride changed";
 * what the screen shows always comes from the API. <c>onChange</c> runs for that message and every time the connection is (re)established,
 * because a message sent while the phone was offline is gone; the result says whether the connection is up, so the caller polls while it is not.
 */
export function useRideLive(enabled: boolean, onChange: () => void): { connected: boolean } {
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    let stopped = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/rides`, {
        accessTokenFactory: async () => (await getApiAccessToken()) ?? '',
      })
      .withAutomaticReconnect(reconnectDelays)
      .configureLogging(LogLevel.None)
      .build();

    connection.on('rideChanged', () => onChange());
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => {
      setConnected(true);
      onChange();
    });
    connection.onclose(() => {
      setConnected(false);
      if (!stopped) {
        retry = setTimeout(start, retryMs);
      }
    });

    function start() {
      connection.start().then(
        () => {
          if (!stopped) {
            setConnected(true);
            onChange();
          }
        },
        () => {
          if (!stopped) {
            setConnected(false);
            retry = setTimeout(start, retryMs);
          }
        },
      );
    }

    start();
    return () => {
      stopped = true;
      if (retry) {
        clearTimeout(retry);
      }
      void connection.stop();
    };
    // onChange is read through the closure on purpose: a new function each render must not tear the connection down.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled]);

  return { connected: enabled && connected };
}
