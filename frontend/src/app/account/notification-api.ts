import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom, map } from 'rxjs';
import { StrictHttpResponse } from '../api/strict-http-response';
import { getCsrf } from '../api/fn/auth/get-csrf';
import { listAccountNotifications } from '../api/fn/account-notifications/list-account-notifications';
import { readAccountNotification } from '../api/fn/account-notifications/read-account-notification';
import { deleteAccountNotification } from '../api/fn/account-notifications/delete-account-notification';
import { getNotificationPreferences } from '../api/fn/account-notifications/get-notification-preferences';
import { saveNotificationPreferences } from '../api/fn/account-notifications/save-notification-preferences';
import { NotificationPreferenceResponse } from '../api/models/notification-preference-response';

@Injectable({ providedIn: 'root' })
export class NotificationApi {
  private readonly http = inject(HttpClient);

  private request<T>(response: Observable<StrictHttpResponse<T>>): Promise<T> {
    return firstValueFrom(response.pipe(map((value) => value.body)));
  }

  private async write<T>(
    operation: (token: string) => Observable<StrictHttpResponse<T>>,
  ): Promise<T> {
    const csrf = await this.request(getCsrf(this.http, ''));
    return this.request(operation(csrf.token));
  }

  list(page = 1) {
    return this.request(listAccountNotifications(this.http, '', { page, pageSize: 20 }));
  }

  preferences() {
    return this.request(getNotificationPreferences(this.http, ''));
  }

  save(preferences: NotificationPreferenceResponse[]) {
    return this.write((token) =>
      saveNotificationPreferences(this.http, '', { body: preferences, 'X-CSRF-TOKEN': token }),
    );
  }

  read(id: string) {
    return this.write((token) =>
      readAccountNotification(this.http, '', { id, 'X-CSRF-TOKEN': token }),
    );
  }

  delete(id: string) {
    return this.write((token) =>
      deleteAccountNotification(this.http, '', { id, 'X-CSRF-TOKEN': token }),
    );
  }
}
