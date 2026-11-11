import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

@Component({
  selector: 'app-upcoming',
  imports: [RouterLink],
  template: `
    <section class="intro" aria-labelledby="upcoming-title">
      <p class="eyebrow">Первый этап</p>
      <h1 id="upcoming-title">{{ title }}</h1>
      <p class="description">Раздел пока в разработке. Доступ появится после запуска аккаунтов.</p>
      <a class="back-link" routerLink="/">Вернуться на главную →</a>
    </section>
  `,
})
export class Upcoming {
  protected readonly title = inject(ActivatedRoute).snapshot.data['title'] as string;
}
