import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../shared/components/card/card.component';
import { BadgeComponent } from '../../shared/components/badge/badge.component';
import { environment } from '../../../environments/environment';

interface EndpointExample {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
  path: string;
  description: string;
}

const ENDPOINTS: EndpointExample[] = [
  { method: 'GET', path: '/api/v1/organizations', description: 'List all organizations the caller has access to.' },
  { method: 'POST', path: '/api/v1/organizations', description: 'Create a new organization (tenant).' },
  { method: 'GET', path: '/api/v1/organizations/{id}', description: 'Retrieve a single organization by ID.' },
  { method: 'PUT', path: '/api/v1/organizations/{id}', description: 'Update organization details or theme.' },
  { method: 'DELETE', path: '/api/v1/organizations/{id}', description: 'Deactivate an organization.' },
  { method: 'GET', path: '/api/v1/users', description: 'List users within the active organization.' },
  { method: 'POST', path: '/api/v1/auth/login', description: 'Authenticate and receive a session token.' },
  { method: 'POST', path: '/api/v1/auth/register', description: 'Register a new organization and admin account.' },
];

const SAMPLE_RESPONSE = `{
  "id": "org-002",
  "name": "ABC International School",
  "code": "ABC-INTL",
  "theme": "green",
  "createdAt": "2026-01-14T09:12:00Z"
}`;

@Component({
  selector: 'app-swagger-page',
  standalone: true,
  imports: [PageHeaderComponent, CardComponent, BadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './swagger.component.html',
  styleUrl: './swagger.component.scss',
})
export class SwaggerPageComponent {
  readonly endpoints = ENDPOINTS;
  readonly apiUrl = environment.apiUrl;
  readonly sampleResponse = SAMPLE_RESPONSE;

  methodVariant(method: EndpointExample['method']): 'success' | 'info' | 'warning' | 'danger' {
    switch (method) {
      case 'GET': return 'info';
      case 'POST': return 'success';
      case 'PUT': return 'warning';
      case 'DELETE': return 'danger';
    }
  }
}
