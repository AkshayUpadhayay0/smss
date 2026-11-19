import * as deviceApi from '../../api/device';
import { showBanner } from './banner';
import { firebasePlatform } from './firebasePlatform';
import { createPushLifecycle } from './lifecycle';
import { createPushService } from './pushCore';

/** The app-wide push lifecycle. The session code calls it; nothing else needs to. */
export const pushLifecycle = createPushLifecycle(
  createPushService({
    platform: firebasePlatform,
    api: { register: deviceApi.registerDevice, unregister: deviceApi.unregisterDevice },
    onForeground: showBanner,
  }),
);
