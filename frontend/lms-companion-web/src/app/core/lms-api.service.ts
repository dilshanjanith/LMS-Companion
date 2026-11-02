import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RuntimeConfigService } from './runtime-config.service';
import {
  ApiPage,
  Assignment,
  Course,
  CourseProgress,
  ConnectionSummary,
  InstitutionSummary,
  ScheduleEvent
} from './models';

@Injectable({ providedIn: 'root' })
export class LmsApiService {
  private readonly baseUrl: string;

  constructor(http: HttpClient, config: RuntimeConfigService) {
    this.baseUrl = `${config.current.apiBaseUrl}/api/v1`;
    this.http = http;
  }

  private readonly http: HttpClient;

  getInstitutions(): Promise<InstitutionSummary[]> {
    return firstValueFrom(this.http.get<InstitutionSummary[]>(`${this.baseUrl}/connections/institutions`));
  }

  getConnections(): Promise<ConnectionSummary[]> {
    return firstValueFrom(this.http.get<ConnectionSummary[]>(`${this.baseUrl}/connections`));
  }

  getCourses(connectionId: string): Promise<ApiPage<Course>> {
    return firstValueFrom(this.http.get<ApiPage<Course>>(`${this.baseUrl}/courses`, {
      params: new HttpParams().set('connectionId', connectionId).set('pageSize', 100)
    }));
  }

  getUpcomingAssignments(connectionId: string, from: Date, to: Date): Promise<ApiPage<Assignment>> {
    return firstValueFrom(this.http.get<ApiPage<Assignment>>(`${this.baseUrl}/assignments/upcoming`, {
      params: new HttpParams()
        .set('connectionId', connectionId)
        .set('from', from.toISOString())
        .set('to', to.toISOString())
        .set('pageSize', 100)
    }));
  }

  getSchedule(connectionId: string, from: Date, to: Date): Promise<ApiPage<ScheduleEvent>> {
    return firstValueFrom(this.http.get<ApiPage<ScheduleEvent>>(`${this.baseUrl}/schedule`, {
      params: new HttpParams()
        .set('connectionId', connectionId)
        .set('from', from.toISOString())
        .set('to', to.toISOString())
        .set('pageSize', 100)
    }));
  }

  getCourseProgress(connectionId: string, courseId: string): Promise<CourseProgress> {
    return firstValueFrom(this.http.get<CourseProgress>(
      `${this.baseUrl}/courses/${encodeURIComponent(courseId)}/progress`,
      { params: new HttpParams().set('connectionId', connectionId) }
    ));
  }

  startCanvasConnection(institutionId: string): Promise<{ authorizationUrl: string }> {
    return firstValueFrom(this.http.post<{ authorizationUrl: string }>(
      `${this.baseUrl}/connections/canvas/start`,
      { institutionId }
    ));
  }

  connectPersonalCanvasToken(institutionId: string, token: string): Promise<ConnectionSummary> {
    return firstValueFrom(this.http.post<ConnectionSummary>(
      `${this.baseUrl}/connections/canvas/personal-token`, { institutionId, token }));
  }

  disconnect(connectionId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/connections/${encodeURIComponent(connectionId)}`));
  }
}
