import { ThemeName } from './theme.model';

export interface Organization {
  id: string;
  name: string;
  code: string;
  logo?: string;
  theme: ThemeName;
  createdAt: string;
}
