// "Not now" on the disclosure lasts until the app is closed: Home does not push the disclosure again during this run, but the next launch shows it again.
let skippedThisRun = false;
export const setupSkipped = (): boolean => skippedThisRun;
export const skipSetupThisRun = (): void => {
  skippedThisRun = true;
};
