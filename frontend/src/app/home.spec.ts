import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Home } from './home';
import { vi } from 'vitest';
import { provideRouter } from '@angular/router';

describe('Home server connection', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows successful connection after the generated API request completes', async () => {
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    const request = http.expectOne('/api/system/info');
    expect(request.request.method).toBe('GET');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[role="status"]')?.textContent,
    ).toContain('Проверяем соединение');
    request.flush({ name: 'CityPortal', stage: 1 });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(
        (fixture.nativeElement as HTMLElement).querySelector('[role="status"]')?.textContent,
      ).toContain('Соединение с сервером установлено');
    });
    expect((fixture.nativeElement as HTMLElement).querySelector('button')).toBeNull();
  });

  it('allows a failed connection to be retried successfully', async () => {
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    http
      .expectOne('/api/system/info')
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    const element = fixture.nativeElement as HTMLElement;
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(element.querySelector('[role="status"]')?.textContent).toContain(
        'Не удалось связаться',
      );
    });
    const retry = element.querySelector('button');
    expect(retry?.textContent).toContain('Повторить');
    retry!.click();
    fixture.detectChanges();
    expect(element.querySelector('[role="status"]')?.textContent).toContain('Проверяем соединение');
    http.expectOne('/api/system/info').flush({ name: 'CityPortal', stage: 1 });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(element.querySelector('[role="status"]')?.textContent).toContain(
        'Соединение с сервером установлено',
      );
    });
    expect(element.querySelector('button')).toBeNull();
  });
});
