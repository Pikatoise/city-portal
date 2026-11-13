import { inject, Injectable, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Api } from './api/api';
import { getSystemInfo } from './api/fn/system/get-system-info';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SiteMetadata {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);
  private readonly title = inject(Title);
  readonly name = signal('CityPortal');
  readonly city = signal('');

  async load(): Promise<void> {
    try {
      const info = (
        await firstValueFrom(getSystemInfo(this.http, this.api.rootUrl).pipe(timeout(3000)))
      ).body;
      this.name.set(info.name);
      this.city.set(info.city);
      this.title.setTitle(info.name);
    } catch {
      // Keep the public shell available when metadata cannot be loaded.
    }
  }
}
