import { afterNextRender, Component, inject, signal } from '@angular/core';
import { Api } from './api/api';
import { getSystemInfo } from './api/fn/system/get-system-info';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  template: `
    <section class="intro" aria-labelledby="home-title">
      <p class="eyebrow">Город начинается с людей</p>
      <h1 id="home-title">Всё, чем живёт город.<br /><span>В одном месте.</span></h1>
      <p class="description">
        Новости, объявления и предложения жителей. Мы готовим портал к запуску.
      </p>
      <div class="notice">
        <span class="notice-icon" aria-hidden="true">01</span>
        <div>
          <h2>Сначала знакомство</h2>
          <p>
            Создайте аккаунт, подтвердите Email и откройте личный кабинет.
            <a routerLink="/account/register">Зарегистрироваться</a> или
            <a routerLink="/account/login">войти</a>.
          </p>
        </div>
      </div>
      <p class="connection" role="status">
        @switch (connection()) {
          @case ('loading') {
            Проверяем соединение с сервером…
          }
          @case ('ready') {
            Соединение с сервером установлено
          }
          @case ('error') {
            Не удалось связаться с сервером.
          }
        }
        @if (connection() === 'error') {
          <button type="button" (click)="checkConnection()">Повторить</button>
        }
      </p>
    </section>
  `,
})
export class Home {
  private readonly api = inject(Api);
  protected readonly connection = signal<'loading' | 'ready' | 'error'>('loading');

  constructor() {
    // Only the browser checks the API; SSR never forwards a user's credentials.
    afterNextRender(() => void this.checkConnection());
  }

  protected async checkConnection(): Promise<void> {
    this.connection.set('loading');
    try {
      await this.api.invoke(getSystemInfo);
      this.connection.set('ready');
    } catch {
      this.connection.set('error');
    }
  }
}
