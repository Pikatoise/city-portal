import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { CurrentWeatherResponse } from '../api/models/current-weather-response';
import { CityClock } from '../staff/city-clock';

const conditions: Record<number, string> = {
  0: 'Ясно',
  1: 'Преимущественно ясно',
  2: 'Переменная облачность',
  3: 'Пасмурно',
  45: 'Туман',
  48: 'Туман с изморозью',
  51: 'Морось',
  53: 'Морось',
  55: 'Сильная морось',
  56: 'Ледяная морось',
  57: 'Ледяная морось',
  61: 'Слабый дождь',
  63: 'Дождь',
  65: 'Сильный дождь',
  66: 'Ледяной дождь',
  67: 'Ледяной дождь',
  71: 'Слабый снег',
  73: 'Снег',
  75: 'Сильный снег',
  77: 'Снежные зёрна',
  80: 'Ливень',
  81: 'Ливень',
  82: 'Сильный ливень',
  85: 'Снежный заряд',
  86: 'Сильный снежный заряд',
  95: 'Гроза',
  96: 'Гроза с градом',
  97: 'Сильная гроза',
  99: 'Гроза с крупным градом',
};

@Component({
  selector: 'app-weather-widget',
  imports: [DecimalPipe],
  template: `
    <aside aria-label="Погода в городе">
      <strong>{{ weather().celsius | number: '1.0-0' }} °C</strong>
      <span>{{ condition() }}</span>
      @if (weather().isStale) {
        <time [attr.datetime]="weather().observedAtUtc">
          Данные на {{ cityClock.format(weather().observedAtUtc) }}
        </time>
      }
      <a href="https://open-meteo.com/">Open-Meteo</a>
    </aside>
  `,
  styles: `
    aside {
      display: flex;
      align-items: baseline;
      flex-wrap: wrap;
      gap: 6px 12px;
      margin-block: 0 16px;
      color: var(--color-muted);
      font-size: 14px;
    }
    strong {
      color: var(--color-ink);
      font-size: 18px;
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class WeatherWidget {
  readonly weather = input.required<CurrentWeatherResponse>();
  protected readonly cityClock = inject(CityClock);

  protected readonly condition = computed(
    () => conditions[Number(this.weather().weatherCode)] ?? 'Нет описания',
  );
}
