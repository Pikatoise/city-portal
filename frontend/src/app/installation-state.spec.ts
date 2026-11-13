import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { App } from './app';
import { routes } from './app.routes';
import { InstallationState } from './installation-state';

@Component({ template: '<h1>Setup only</h1>' })
class SetupDestination {}

describe('Installation page access', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    const testRoutes: Routes = routes.map((route) =>
      route.path === 'setup' ? { path: 'setup', component: SetupDestination } : route,
    );
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(testRoutes)],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  async function state(installed: boolean): Promise<InstallationState> {
    const installation = TestBed.inject(InstallationState);
    const loading = installation.load();
    http.expectOne('/api/installation/status').flush({ installed, canInstall: !installed });
    await loading;
    return installation;
  }

  it('PendingSetup_HidesNavigationAndLogoLinkUntilCompletion', async () => {
    const installation = await state(false);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav')).toBeNull();
    expect(fixture.nativeElement.querySelector('a.brand')).toBeNull();
    expect(fixture.nativeElement.querySelector('footer')).toBeNull();
    installation.complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('nav a').length).toBe(3);
    expect(fixture.nativeElement.querySelector('a.brand')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('footer')).not.toBeNull();
  });

  it('PendingSetup_RedirectsEveryNormalRouteBeforeItsPageCanRender', async () => {
    await state(false);
    const harness = await RouterTestingHarness.create('/account/login');
    for (const path of [
      '/',
      '/account/register',
      '/account/forgot-password',
      '/account',
      '/staff/accounts',
      '/unknown',
    ]) {
      await harness.navigateByUrl(path);
      expect(TestBed.inject(Router).url).toBe('/setup');
      expect(harness.routeNativeElement?.textContent).toContain('Setup only');
      expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
    }
  });

  it('FailedStatus_KeepsOtherPagesAndNavigationClosed', async () => {
    const installation = TestBed.inject(InstallationState);
    const loading = installation.load();
    http
      .expectOne('/api/installation/status')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await loading;
    await RouterTestingHarness.create('/account/login');
    expect(TestBed.inject(Router).url).toBe('/setup');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('router-outlet')).toBeNull();
  });

  it('CompletedSetup_AllowsLoginPageWithoutRedirectingToWizard', async () => {
    await state(true);
    const harness = await RouterTestingHarness.create('/account/login');
    expect(TestBed.inject(Router).url).toBe('/account/login');
    expect(harness.routeNativeElement?.querySelector('#login-email')).not.toBeNull();
  });
});
