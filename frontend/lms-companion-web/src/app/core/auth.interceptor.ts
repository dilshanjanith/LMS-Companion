import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';
import { RuntimeConfigService } from './runtime-config.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBaseUrl = inject(RuntimeConfigService).current.apiBaseUrl;
  const auth = inject(AuthService);

  try {
    const apiUrl = new URL(apiBaseUrl);
    const requestUrl = new URL(request.url, window.location.origin);
    const apiPath = apiUrl.pathname.replace(/\/$/, '');
    const isApiRequest = requestUrl.origin === apiUrl.origin
      && (requestUrl.pathname === apiPath || requestUrl.pathname.startsWith(`${apiPath}/`));
    if (!isApiRequest) return next(request);
  } catch {
    return next(request);
  }

  const token = auth.getAccessToken();
  return next(token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request);
};
