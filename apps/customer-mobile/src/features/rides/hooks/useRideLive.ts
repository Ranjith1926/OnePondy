import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useEffect, useState } from 'react';

import { getApiAccessToken } from '../../../api/apiClient';
import { apiBaseUrl } from '../../../api/config';

/** How long to wait before trying again once the connection has given up. */
const retryMs = 30_000;

/** Waits (milliseconds) between the connection's own attempts to come back after it drops. */
const reconnectDelays = [0, 2_000, 5_000, 10_000];

/**
 * Live updates for the rider's ride (spec Phase 9: SignalR updates, reconnect behaviour and API refetch). The hub sends one message, "this ride
 * changed", with nothing else in it, so what the screen shows always comes from the API. This hook calls <c>onChange</c> for that message and
 * once more every time the connection is (re)established, because a message sent while the phone was offline is gone; and says whether the
 * connection is up, so the caller can poll while it is not.
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

  // Switched off means not connected, whatever the last connection said.
  return { connected: enabled && connected };
}
