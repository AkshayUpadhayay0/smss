import { Injectable } from '@angular/core';
import { Organization, ThemeName } from '../models';
import { readStorage, STORAGE_KEYS, writeStorage } from '../utils/storage.util';
import { generateId } from '../utils/id.util';

/**
 * Reads/writes organizations from the mock localStorage "database".
 * Swap the bodies of these methods for HttpClient calls later without
 * touching any component.
 */
@Injectable({ providedIn: 'root' })
export class OrganizationService {
  getAll(): Organization[] {
    return readStorage<Organization[]>(STORAGE_KEYS.organizations, []);
  }

  getById(id: string): Organization | undefined {
    return this.getAll().find((org) => org.id === id);
  }

  codeExists(code: string): boolean {
    return this.getAll().some((org) => org.code.toLowerCase() === code.toLowerCase());
  }

  create(name: string, code: string, theme: ThemeName): Organization {
    const org: Organization = {
      id: generateId('org'),
      name,
      code,
      theme,
      createdAt: new Date().toISOString(),
    };
    const all = this.getAll();
    all.unshift(org);
    writeStorage(STORAGE_KEYS.organizations, all);
    return org;
  }

  updateTheme(id: string, theme: ThemeName): void {
    const all = this.getAll();
    const idx = all.findIndex((org) => org.id === id);
    if (idx === -1) return;
    all[idx] = { ...all[idx], theme };
    writeStorage(STORAGE_KEYS.organizations, all);
  }
}
