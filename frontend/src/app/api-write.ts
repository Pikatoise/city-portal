import { isPlatformBrowser } from '@angular/common';
import { inject, Injectable, PLATFORM_ID } from '@angular/core';
import { Api, ApiFnRequired } from './api/api';
import { getCsrf } from './api/fn/auth/get-csrf';
import { CsrfResponse } from './api/models/csrf-response';
import { StrictHttpResponse } from './api/strict-http-response';

type CsrfHeader = { 'X-CSRF-TOKEN': string };

@Injectable({ providedIn: 'root' })
export class ApiWrite {
  private readonly api = inject(Api);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private pendingToken?: Promise<StrictHttpResponse<CsrfResponse>>;

  async invoke<P extends Partial<CsrfHeader>, R>(
    operation: ApiFnRequired<P, R>,
    params: Omit<P, keyof CsrfHeader>,
  ): Promise<R> {
    if (!this.isBrowser) throw new Error('API writes require a browser session.');

    // The first response must set the antiforgery cookie before the next token request.
    const fetchToken = () => this.api.invoke$Response(getCsrf);
    const request = this.pendingToken?.then(fetchToken, fetchToken) ?? fetchToken();
    this.pendingToken = request;
    let token: string;
    try {
      token = (await request).body.token;
    } finally {
      if (this.pendingToken === request) this.pendingToken = undefined;
    }
    const response = await this.api.invoke$Response(operation, {
      ...params,
      'X-CSRF-TOKEN': token,
    } as P);
    return response.body;
  }
}
