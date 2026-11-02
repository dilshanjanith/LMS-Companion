import { TestBed } from '@angular/core/testing';
import { ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { LmsApiService } from '../core/lms-api.service';
import { RuntimeConfigService } from '../core/runtime-config.service';
import { DashboardPage } from './dashboard.page';

describe('DashboardPage', () => {
  let fixture: ComponentFixture<DashboardPage>;
  let api: {
    getInstitutions: ReturnType<typeof vi.fn>;
    getConnections: ReturnType<typeof vi.fn>;
    getCourses: ReturnType<typeof vi.fn>;
    getUpcomingAssignments: ReturnType<typeof vi.fn>;
    getSchedule: ReturnType<typeof vi.fn>;
    startCanvasConnection: ReturnType<typeof vi.fn>;
    connectPersonalCanvasToken: ReturnType<typeof vi.fn>;
    disconnect: ReturnType<typeof vi.fn>;
    getCourseProgress: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    api = {
      getInstitutions: vi.fn().mockResolvedValue([{ id: 'north-campus', name: 'North Campus', provider: 'Canvas', baseUrl: 'https://school.instructure.com/', canConnect: true }]),
      getConnections: vi.fn().mockResolvedValue([]),
      getCourses: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, totalItems: 0 }),
      getUpcomingAssignments: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, totalItems: 0 }),
      getSchedule: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, totalItems: 0 }),
      startCanvasConnection: vi.fn(),
      connectPersonalCanvasToken: vi.fn(),
      disconnect: vi.fn(),
      getCourseProgress: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [DashboardPage],
      providers: [
        provideRouter([]),
        { provide: LmsApiService, useValue: api },
        { provide: AuthService, useValue: { displayName: () => 'Ari Student', signOut: vi.fn() } },
        { provide: RuntimeConfigService, useValue: { current: { canvasInstitutionId: 'north-campus' } } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardPage);
  });

  it('shows an accessible connect state when no Canvas account exists', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Let’s bring your Canvas courses in.');
    expect(fixture.nativeElement.querySelector('[aria-label="Connected learning accounts"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[aria-live="polite"]')).toBeTruthy();
  });

  it('loads and displays courses and upcoming assignments for a single connected account', async () => {
    api.getConnections.mockResolvedValue([
      { id: 'connection-1', provider: 0, baseUrl: 'https://school.instructure.com', status: 0, expiresAt: null }
    ]);
    api.getCourses.mockResolvedValue({
      items: [{ id: 'course-1', provider: 'Canvas', name: 'Ecology', code: 'BIO 210', term: 'Fall', role: 'Student', url: null }],
      page: 1,
      pageSize: 100,
      totalItems: 1
    });
    api.getUpcomingAssignments.mockResolvedValue({
      items: [{ id: 'assignment-1', courseId: 'course-1', title: 'Field notes', dueAt: '2026-11-02T12:00:00Z', status: null, pointsPossible: 20, url: null }],
      page: 1,
      pageSize: 100,
      totalItems: 1
    });

    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(api.getCourses).toHaveBeenCalledWith('connection-1');
    expect(api.getUpcomingAssignments).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.querySelector('.hero-card')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Ecology');
    expect(fixture.nativeElement.textContent).toContain('Field notes');
  });

  it('requires an explicit choice when there are multiple connected Canvas accounts', async () => {
    api.getConnections.mockResolvedValue([
      { id: 'connection-1', provider: 0, baseUrl: 'https://one.instructure.com', status: 0, expiresAt: null },
      { id: 'connection-2', provider: 0, baseUrl: 'https://two.instructure.com', status: 0, expiresAt: null }
    ]);

    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Choose a learning space');
    expect(api.getCourses).not.toHaveBeenCalled();
  });

  it('shows pending university approval and prevents authorization without a developer key', async () => {
    api.getInstitutions.mockResolvedValue([{ id: 'stanford-seed', name: 'Stanford SEED', provider: 'Canvas', baseUrl: 'https://seedonline.stanford.edu/', canConnect: false }]);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(fixture.componentInstance.canConnect()).toBe(false);
    await fixture.componentInstance.connectCanvas();
    expect(api.startCanvasConnection).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('awaiting university approval');
  });

  it('requires an institution selection instead of guessing between universities', async () => {
    api.getInstitutions.mockResolvedValue([
      { id: 'one', name: 'One', provider: 'Canvas', baseUrl: 'https://one.example/', canConnect: true },
      { id: 'two', name: 'Two', provider: 'Canvas', baseUrl: 'https://two.example/', canConnect: true }
    ]);
    await fixture.componentInstance.ngOnInit();
    expect(fixture.componentInstance.selectedInstitutionId()).toBe('');
    expect(fixture.componentInstance.canConnect()).toBe(false);
  });

  it('rejects an authorization redirect to an unexpected host', async () => {
    await fixture.componentInstance.ngOnInit();
    api.startCanvasConnection.mockResolvedValue({ authorizationUrl: 'https://unexpected.example/login/oauth2/auth' });
    await fixture.componentInstance.connectCanvas();
    expect(fixture.componentInstance.error()).toContain('could not be started');
    expect(fixture.componentInstance.connecting()).toBe(false);
  });
});
