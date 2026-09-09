import { Injectable, signal } from '@angular/core';

export interface SchoolBranding {
  name: string;
  shortName: string;
  tagline: string;
  logoInitials: string;
  address: string;
}

const STORAGE_KEY = 'smt-branding';

@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly defaults: SchoolBranding = {
    name: 'Green Valley Public School',
    shortName: 'GVPS',
    tagline: 'Nurturing Minds, Building Futures',
    logoInitials: 'GV',
    address: '42 Rajpur Road, Dehradun, Uttarakhand 248001',
  };

  readonly branding = signal<SchoolBranding>(this.load());

  update(partial: Partial<SchoolBranding>): void {
    const next = { ...this.branding(), ...partial };
    this.branding.set(next);
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    }
  }

  private load(): SchoolBranding {
    if (typeof localStorage === 'undefined') return this.defaults;
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? { ...this.defaults, ...JSON.parse(raw) } : this.defaults;
    } catch {
      return this.defaults;
    }
  }
}
