import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom, map } from 'rxjs';
import { StrictHttpResponse } from '../api/strict-http-response';
import { getCsrf } from '../api/fn/auth/get-csrf';
import { listOwnArticles } from '../api/fn/editorial/list-own-articles';
import { getOwnArticle } from '../api/fn/editorial/get-own-article';
import { createArticleDraft } from '../api/fn/editorial/create-article-draft';
import { saveArticleDraft } from '../api/fn/editorial/save-article-draft';
import { deleteArticleDraft } from '../api/fn/editorial/delete-article-draft';
import { listEditorialTaxonomy } from '../api/fn/editorial/list-editorial-taxonomy';
import { createEditorialTaxonomy } from '../api/fn/editorial/create-editorial-taxonomy';
import { saveEditorialTaxonomy } from '../api/fn/editorial/save-editorial-taxonomy';
import { actOnArticleReview } from '../api/fn/editorial/act-on-article-review';
import { listArticleReviewQueue } from '../api/fn/editorial/list-article-review-queue';
import { getArticleReview } from '../api/fn/editorial/get-article-review';
import { getArticleHistory } from '../api/fn/editorial/get-article-history';
import { ArticleContent } from '../api/models/article-content';
import { TaxonomyResponse } from '../api/models/taxonomy-response';
import { errorMessage } from '../account/account-api';
export type { ArticleContent } from '../api/models/article-content';
export type { ArticleImage } from '../api/models/article-image';
export type { ArticleResponse } from '../api/models/article-response';
export type { ArticleSummaryResponse } from '../api/models/article-summary-response';
export type { TaxonomyResponse } from '../api/models/taxonomy-response';
export type { ReviewResponse } from '../api/models/review-response';
export type { ReviewSummaryResponse } from '../api/models/review-summary-response';
export type { ArticleHistoryResponse } from '../api/models/article-history-response';

@Injectable({ providedIn: 'root' })
export class NewsApi {
  private readonly http = inject(HttpClient);
  private request<T>(response: Observable<StrictHttpResponse<T>>): Promise<T> {
    return firstValueFrom(response.pipe(map((value) => value.body)));
  }
  private async write<T>(
    operation: (token: string) => Observable<StrictHttpResponse<T>>,
  ): Promise<T> {
    const csrf = await this.request(getCsrf(this.http, ''));
    return this.request(operation(csrf.token));
  }
  list(page = 1, search = '') {
    return this.request(listOwnArticles(this.http, '', { page, pageSize: 20, search }));
  }
  get(id: string) {
    return this.request(getOwnArticle(this.http, '', { id }));
  }
  create() {
    return this.write((token) => createArticleDraft(this.http, '', { 'X-CSRF-TOKEN': token }));
  }
  save(id: string, version: number | string, content: ArticleContent) {
    return this.write((token) =>
      saveArticleDraft(this.http, '', { id, body: { version, content }, 'X-CSRF-TOKEN': token }),
    );
  }
  delete(id: string, version: number | string) {
    return this.write((token) =>
      deleteArticleDraft(this.http, '', { id, version, 'X-CSRF-TOKEN': token }),
    );
  }
  act(id: string, version: number | string, operation: string, reason = '') {
    return this.write((token) =>
      actOnArticleReview(this.http, '', {
        id,
        operation,
        body: { version, reason },
        'X-CSRF-TOKEN': token,
      }),
    );
  }
  reviews(mine = false, page = 1) {
    return this.request(listArticleReviewQueue(this.http, '', { mine, page, pageSize: 20 }));
  }
  review(id: string) {
    return this.request(getArticleReview(this.http, '', { id }));
  }
  history(id: string, page = 1) {
    return this.request(getArticleHistory(this.http, '', { id, page, pageSize: 20 }));
  }
  async taxonomy(kind: string): Promise<TaxonomyResponse[]> {
    const entries: TaxonomyResponse[] = [];
    for (let page = 1; page <= 100000; page++) {
      const result = await this.request(
        listEditorialTaxonomy(this.http, '', { kind, page, pageSize: 100 }),
      );
      entries.push(...result.items);
      if (entries.length >= Number(result.total) || result.items.length === 0) break;
    }
    return entries;
  }
  createTaxonomy(kind: string, name: string) {
    return this.write((token) =>
      createEditorialTaxonomy(this.http, '', { kind, body: { name }, 'X-CSRF-TOKEN': token }),
    );
  }
  updateTaxonomy(entry: TaxonomyResponse, name: string, archived: boolean) {
    return this.write((token) =>
      saveEditorialTaxonomy(this.http, '', {
        kind: entry.kind,
        id: entry.id,
        body: { name, archived, version: entry.version },
        'X-CSRF-TOKEN': token,
      }),
    );
  }
}

export function newsError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 401) return 'Войди в аккаунт, чтобы продолжить работу.';
    if (error.status === 403) return 'Нужна действующая роль и подтверждённый Email.';
    if (error.status === 404) return 'Материал недоступен.';
    if (error.status === 409)
      return 'Данные изменились. Сохрани копию своих правок и загрузи актуальную версию.';
    if (error.status === 400) {
      const fields = error.error?.errors as Record<string, string[]> | undefined;
      if (fields) {
        const labels: Record<string, string> = {
          title: 'заголовок',
          summary: 'краткое описание',
          body: 'текст',
          images: 'изображения',
          taxonomy: 'категория и теги',
          sources: 'источники',
          alt: 'альтернативный текст',
          caption: 'подпись',
          seoTitle: 'SEO-заголовок',
          seoDescription: 'SEO-описание',
          link: 'ссылка',
          name: 'название',
          content: 'поля статьи',
          submission: 'обязательные поля',
          page: 'номер страницы',
        };
        return (
          'Проверь поля: ' +
          Object.keys(fields)
            .map((field) => labels[field] ?? 'данные')
            .join(', ') +
          '. Изменения не сохранены.'
        );
      }
    }
  }
  return errorMessage(error);
}
