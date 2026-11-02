import { HttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';
import { LmsApiService } from './lms-api.service';
import { RuntimeConfigService } from './runtime-config.service';

describe('LmsApiService', () => {
  let api: LmsApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: RuntimeConfigService, useValue: { current: { apiBaseUrl: 'https://api.example.test' } } },
        { provide: AuthService, useValue: { getAccessToken: vi.fn().mockReturnValue('signed-in-access-token') } }
      ]
    });
    api = TestBed.inject(LmsApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends an authenticated, connection-scoped course request', async () => {
    const response = api.getCourses('connection-7');
    const request = http.expectOne('https://api.example.test/api/v1/courses?connectionId=connection-7&pageSize=100');
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer signed-in-access-token');
    request.flush({ items: [], page: 1, pageSize: 100, totalItems: 0 });
    await expect(response).resolves.toEqual({ items: [], page: 1, pageSize: 100, totalItems: 0 });
  });

  it('does not send the access token to a host that only shares the API URL prefix', () => {
    TestBed.inject(HttpClient).get('https://api.example.test.attacker.invalid/api/v1/courses').subscribe();
    const request = http.expectOne('https://api.example.test.attacker.invalid/api/v1/courses');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });
});
