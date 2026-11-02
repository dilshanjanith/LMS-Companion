import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../core/auth.service';
import { LmsApiService } from '../core/lms-api.service';
import { RuntimeConfigService } from '../core/runtime-config.service';
import { Assignment, ConnectionSummary, Course, CourseProgress, ScheduleEvent, InstitutionSummary } from '../core/models';

type DashboardSection = 'overview' | 'courses' | 'assignments' | 'schedule';

@Component({
  standalone: true,
  selector: 'app-dashboard-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './dashboard.page.html'
})
export class DashboardPage {
  readonly auth = inject(AuthService);
  readonly config = inject(RuntimeConfigService);
  private readonly api = inject(LmsApiService);

  readonly activeSection = signal<DashboardSection>('overview');
  readonly loading = signal(true);
  readonly connecting = signal(false);
  readonly personalToken = signal('');
  readonly connections = signal<ConnectionSummary[]>([]);
  readonly institutions = signal<InstitutionSummary[]>([]);
  readonly selectedInstitutionId = signal('');
  readonly selectedConnectionId = signal<string | null>(null);
  readonly courses = signal<Course[]>([]);
  readonly assignments = signal<Assignment[]>([]);
  readonly schedule = signal<ScheduleEvent[]>([]);
  readonly selectedCourse = signal<Course | null>(null);
  readonly progress = signal<CourseProgress | null>(null);
  readonly loadingProgress = signal(false);
  readonly error = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);
  readonly expandedConnectionId = signal<string | null>(null);
  readonly today = new Date();
  readonly navigation: { id: DashboardSection; label: string; icon: string }[] = [
    { id: 'overview', label: 'Overview', icon: '◫' },
    { id: 'courses', label: 'My courses', icon: '▤' },
    { id: 'assignments', label: 'Assignments', icon: '✓' },
    { id: 'schedule', label: 'Class schedule', icon: '▦' }
  ];

  async ngOnInit(): Promise<void> {
    const canvasResult = new URLSearchParams(window.location.search).get('canvas');
    if (canvasResult) window.history.replaceState({}, document.title, '/dashboard');
    await this.refresh();
    if (canvasResult === 'connected') this.actionMessage.set('Canvas authorization completed. You can return to your AI chat and ask for your courses.');
    if (canvasResult === 'error') this.error.set('Canvas authorization could not be completed. You can try again.');
  }

  async refresh(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    this.actionMessage.set(null);
    try {
      const [connections, institutions] = await Promise.all([this.api.getConnections(), this.api.getInstitutions()]);
      this.institutions.set(institutions);
      if (!institutions.some(item => item.id === this.selectedInstitutionId())) {
        this.selectedInstitutionId.set(institutions.length === 1 ? institutions[0].id : '');
      }
      this.connections.set(connections);
      const available = connections.filter(connection => this.isConnected(connection.status));
      const previousSelection = this.selectedConnectionId();
      const selected = available.length === 1
        ? available[0].id
        : available.some(connection => connection.id === previousSelection)
          ? previousSelection
          : null;

      this.selectedConnectionId.set(selected);
      if (!selected) {
        this.clearLearningData();
        return;
      }

      await this.loadLearningData(selected);
    } catch (error: unknown) {
      this.error.set(this.describeError(error));
      this.clearLearningData();
    } finally {
      this.loading.set(false);
    }
  }

  async onConnectionChange(event: Event): Promise<void> {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedConnectionId.set(value || null);
    this.actionMessage.set(null);
    if (value) {
      this.loading.set(true);
      this.error.set(null);
      try {
        await this.loadLearningData(value);
      } catch (error: unknown) {
        this.error.set(this.describeError(error));
        this.clearLearningData();
      } finally {
        this.loading.set(false);
      }
    } else {
      this.clearLearningData();
    }
  }

  async connectCanvas(): Promise<void> {
    const institutionId = this.selectedInstitutionId();
    const institution = this.institutions().find(item => item.id === institutionId);
    if (!institution?.canConnect) {
      this.error.set(institution ? 'This institution is awaiting administrator approval.' : 'Choose an institution to continue.');
      return;
    }

    this.error.set(null);
    this.connecting.set(true);
    try {
      const { authorizationUrl } = await this.api.startCanvasConnection(institutionId);
      const destination = new URL(authorizationUrl);
      if (destination.protocol !== 'https:' || destination.origin !== new URL(institution.baseUrl).origin
          || destination.pathname !== '/login/oauth2/auth' || destination.username || destination.password) {
        throw new Error('Unexpected Canvas authorization destination.');
      }
      window.location.assign(destination.toString());
    } catch (error: unknown) {
      this.error.set(this.describeError(error, 'Canvas authorization could not be started. Try again later.'));
      this.connecting.set(false);
    }
  }

  canConnect(): boolean {
    return !this.loading() && !this.connecting()
      && this.institutions().some(item => item.id === this.selectedInstitutionId() && item.canConnect);
  }

  async connectPersonalToken(): Promise<void> {
    const token = this.personalToken().trim();
    const institutionId = this.selectedInstitutionId();
    if (!token || !institutionId) return;
    this.connecting.set(true);
    this.error.set(null);
    try {
      await this.api.connectPersonalCanvasToken(institutionId, token);
      this.personalToken.set('');
      await this.refresh();
      this.actionMessage.set('Canvas connected. Return to your AI chat to ask about your course.');
    } catch (error: unknown) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      this.error.set(status === 422 ? 'Canvas rejected this token. No connection was saved.' : 'The Canvas token could not be connected.');
    } finally {
      this.personalToken.set('');
      this.connecting.set(false);
    }
  }

  selectInstitution(event: Event): void {
    this.selectedInstitutionId.set((event.target as HTMLSelectElement).value);
  }

  async disconnect(connection: ConnectionSummary): Promise<void> {
    if (!window.confirm('Disconnect this Canvas account? The dashboard will stop showing data from it.')) return;

    this.error.set(null);
    this.actionMessage.set(null);
    this.expandedConnectionId.set(null);
    try {
      await this.api.disconnect(connection.id);
      this.selectedConnectionId.set(null);
      await this.refresh();
      this.actionMessage.set('The Canvas account has been disconnected.');
    } catch (error: unknown) {
      this.error.set(this.describeError(error, 'The Canvas account could not be disconnected. Try again.'));
    }
  }

  async toggleProgress(course: Course): Promise<void> {
    if (this.selectedCourse()?.id === course.id) {
      this.selectedCourse.set(null);
      this.progress.set(null);
      return;
    }

    const connectionId = this.selectedConnectionId();
    if (!connectionId) return;
    this.selectedCourse.set(course);
    this.progress.set(null);
    this.loadingProgress.set(true);
    try {
      this.progress.set(await this.api.getCourseProgress(connectionId, course.id));
    } catch (error: unknown) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      this.progress.set({
        courseId: course.id,
        completionPercent: null,
        completedItems: null,
        totalItems: null,
        explanation: status === 422
          ? 'Canvas does not provide progress information for this course.'
          : 'Progress is temporarily unavailable. Try again later.'
      });
    } finally {
      this.loadingProgress.set(false);
    }
  }

  async signOut(): Promise<void> {
    await this.auth.signOut();
  }

  isConnected(status: ConnectionSummary['status']): boolean {
    return status === 'Connected' || status === 0;
  }

  statusLabel(status: ConnectionSummary['status']): string {
    if (status === 'Connected' || status === 0) return 'Connected';
    if (status === 'ReconnectRequired' || status === 1) return 'Reconnect needed';
    return 'Disconnected';
  }

  providerLabel(provider: string | number): string {
    return typeof provider === 'number' ? 'Canvas' : provider;
  }

  connectedCount(): number {
    return this.connections().filter(connection => this.isConnected(connection.status)).length;
  }

  reconnectRequiredCount(): number {
    return this.connections().filter(connection => connection.status === 'ReconnectRequired' || connection.status === 1).length;
  }

  courseName(courseId: string): string {
    return this.courses().find(course => course.id === courseId)?.name ?? 'Course';
  }

  dueLabel(dueAt: string | null): string {
    if (!dueAt) return 'Due date not set';
    const dueDate = new Date(dueAt);
    if (Number.isNaN(dueDate.getTime())) return 'Due date unavailable';
    const today = new Date();
    const tomorrow = new Date(today);
    tomorrow.setDate(today.getDate() + 1);
    if (dueDate.toDateString() === today.toDateString()) return 'Today';
    if (dueDate.toDateString() === tomorrow.toDateString()) return 'Tomorrow';
    return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(dueDate);
  }

  selectSection(section: DashboardSection): void {
    this.activeSection.set(section);
  }

  toggleConnectionManager(): void {
    this.expandedConnectionId.set(
      this.expandedConnectionId() ? null : this.connections()[0]?.id ?? null
    );
  }

  trackConnection(_index: number, connection: ConnectionSummary): string {
    return connection.id;
  }

  private async loadLearningData(connectionId: string): Promise<void> {
    const from = new Date();
    const to = new Date(from.getTime() + 30 * 24 * 60 * 60 * 1000);
    const [courses, assignments, schedule] = await Promise.all([
      this.api.getCourses(connectionId),
      this.api.getUpcomingAssignments(connectionId, from, to),
      this.api.getSchedule(connectionId, from, to)
    ]);
    this.courses.set(courses.items);
    this.assignments.set(assignments.items);
    this.schedule.set(schedule.items);
    this.selectedCourse.set(null);
    this.progress.set(null);
  }

  private clearLearningData(): void {
    this.courses.set([]);
    this.assignments.set([]);
    this.schedule.set([]);
    this.selectedCourse.set(null);
    this.progress.set(null);
  }

  private describeError(error: unknown, fallback = 'Your learning data could not be loaded. Please try again.'): string {
    if (!(error instanceof HttpErrorResponse)) return fallback;
    if (error.status === 401) return 'Your sign-in has expired. Sign in again to continue.';
    if (error.status === 403) return 'Your account does not have access to this learning data.';
    if (error.status === 409) {
      const code = (error.error as { error?: string } | null)?.error;
      if (code === 'lms_connection_required') return 'Connect a Canvas account to see your learning data.';
      if (code === 'connection_selection_required') return 'Choose the Canvas account you want to view.';
    }
    if (error.status === 503 || error.status === 429) return 'Canvas is temporarily unavailable. Please try again in a little while.';
    return fallback;
  }
}
