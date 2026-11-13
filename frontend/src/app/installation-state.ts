import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { CanActivateChildFn, Router } from '@angular/router';
import { firstValueFrom, timeout } from 'rxjs';
import { Api } from './api/api';
import { getInstallationStatus } from './api/fn/installation/get-installation-status';
import { InstallationStatusResponse } from './api/models/installation-status-response';

@Injectable({ providedIn: 'root' })
export class InstallationState {
  private readonly http = inject(HttpClient);
  private readonly api = inject(Api);
  readonly status = signal<InstallationStatusResponse | null>(null);
  readonly failed = signal(false);
  readonly installed = computed(() => this.status()?.installed === true);

  async load(): Promise<InstallationStatusResponse | null> {
    this.failed.set(false);
    try {
      const status = (
        await firstValueFrom(getInstallationStatus(this.http, this.api.rootUrl).pipe(timeout(3000)))
      ).body;
      this.status.set(status);
      return status;
    } catch {
      this.status.set(null);
      this.failed.set(true);
      return null;
    }
  }

  complete(): void {
    this.status.set({ installed: true, canInstall: false });
    this.failed.set(false);
  }
}

export const requireInstallation: CanActivateChildFn = async () => {
  const installation = inject(InstallationState);
  const router = inject(Router);
  const status =
    installation.status() ?? (installation.failed() ? null : await installation.load());
  return status?.installed === true || router.parseUrl('/setup');
};
