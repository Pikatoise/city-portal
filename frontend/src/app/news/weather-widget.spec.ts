import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { CityClock } from '../staff/city-clock';
import { PublicNewsApi, publicNewsRequests } from './public-news-api';
import { WeatherWidget } from './weather-widget';

const observedAtUtc = '2026-11-26T10:00:00Z';

describe('City weather', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows the conditions and attribution, and labels the stale reading with its local time', () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: CityClock,
          useValue: {
            format: (value: string) => (value === observedAtUtc ? '26 нояб., 15:00' : ''),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(WeatherWidget);
    const weather = { celsius: -5.25, weatherCode: 73, observedAtUtc, isStale: false };
    fixture.componentRef.setInput('weather', weather);
    fixture.detectChanges();
    const widget: HTMLElement = fixture.nativeElement;
    expect(widget.querySelector('strong')?.textContent?.trim()).toBe('-5 °C');
    expect(widget.querySelector('aside span')?.textContent?.trim()).toBe('Снег');
    expect(widget.querySelector('a')?.getAttribute('href')).toBe('https://open-meteo.com/');
    expect(widget.querySelector('time')).toBeNull();

    fixture.componentRef.setInput('weather', { ...weather, isStale: true });
    fixture.detectChanges();
    const time = widget.querySelector('time');
    expect(time?.dateTime).toBe(observedAtUtc);
    expect(time?.textContent?.trim()).toBe('Данные на 26 нояб., 15:00');
  });

  it('omits credentials and hides the weather for missing or failed observations', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([publicNewsRequests])),
        provideHttpClientTesting(),
      ],
    });
    const api = TestBed.inject(PublicNewsApi);
    const http = TestBed.inject(HttpTestingController);
    const disabled = api.weather();
    const request = http.expectOne('/api/weather/current');
    expect(request.request.credentials).toBe('omit');
    expect(request.request.transferCache).toBe(false);
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(await disabled).toBeNull();

    const failed = api.weather();
    http.expectOne('/api/weather/current').flush('unavailable', {
      status: 503,
      statusText: 'Unavailable',
    });
    expect(await failed).toBeNull();
    http.verify();
  });
});
