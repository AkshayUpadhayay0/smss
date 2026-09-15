import { Injectable } from '@angular/core';
import { RegisteredAccount, Organization } from '../models';
import { DEMO_ACCOUNT, DEMO_ORGANIZATION, SAMPLE_ORGANIZATIONS } from '../mock';
import { readStorage, STORAGE_KEYS, writeStorage } from '../utils/storage.util';

/**
 * Ensures the localStorage "database" has a demo organization + admin
 * account the first time the app runs, so the template can be evaluated
 * immediately without going through registration.
 */
@Injectable({ providedIn: 'root' })
export class SeedService {
  seedIfNeeded(): void {
    const alreadySeeded = readStorage<boolean>(STORAGE_KEYS.seeded, false);
    if (alreadySeeded) return;

    writeStorage<Organization[]>(STORAGE_KEYS.organizations, SAMPLE_ORGANIZATIONS);
    writeStorage<RegisteredAccount[]>(STORAGE_KEYS.accounts, [DEMO_ACCOUNT]);
    writeStorage(STORAGE_KEYS.seeded, true);
  }

  getDemoOrganization(): Organization {
    return DEMO_ORGANIZATION;
  }
}
