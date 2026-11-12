import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { getSystemInfo } from '../api/fn/system/get-system-info';

@Injectable({ providedIn: 'root' })
export class CityClock {
  readonly timeZone = signal('UTC');

  constructor() {
    const http = inject(HttpClient);
    void firstValueFrom(getSystemInfo(http, ''))
      .then((response) => this.timeZone.set(response.body.timeZone))
      .catch(() => {
        // Keep explicitly labelled UTC if metadata is temporarily unavailable.
        this.timeZone.set('UTC');
      });
  }

  format(value: string): string {
    return new Intl.DateTimeFormat('ru-RU', {
      timeZone: this.timeZone(),
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
