import { Component, RESPONSE_INIT, inject } from '@angular/core';

@Component({
  selector: 'app-missing-article',
  template: '<main><h1>Материал не найден</h1></main>',
})
export class MissingArticle {
  constructor() {
    const response = inject(RESPONSE_INIT, { optional: true });
    if (response) response.status = 404;
  }
}
