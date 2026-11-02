export type ConnectionStatus = 'Connected' | 'ReconnectRequired' | 'Disconnected' | 0 | 1 | 2;

export interface InstitutionSummary {
  id: string;
  name: string;
  provider: string;
  baseUrl: string;
  canConnect: boolean;
}

export interface ConnectionSummary {
  id: string;
  provider: string | number;
  baseUrl: string;
  status: ConnectionStatus;
  expiresAt: string | null;
}

export interface ApiPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
}

export interface Course {
  id: string;
  provider: string;
  name: string;
  code: string | null;
  term: string | null;
  role: string | null;
  url: string | null;
}

export interface Assignment {
  id: string;
  courseId: string;
  title: string;
  dueAt: string | null;
  status: string | null;
  pointsPossible: number | null;
  url: string | null;
}

export interface ScheduleEvent {
  id: string;
  courseId: string | null;
  title: string;
  startsAt: string;
  endsAt: string | null;
  location: string | null;
  url: string | null;
}

export interface CourseProgress {
  courseId: string;
  completionPercent: number | null;
  completedItems: number | null;
  totalItems: number | null;
  explanation: string | null;
}

export interface RuntimeConfiguration {
  apiBaseUrl: string;
  oidc: {
    authority: string;
    clientId: string;
    scope: string;
    audience: string;
  };
  canvasInstitutionId: string;
  allowPersonalTokenTesting?: boolean;
}
