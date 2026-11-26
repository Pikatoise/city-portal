import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../api/api';
import { getPublicArticle } from '../api/fn/news/get-public-article';
import { getPublicHomepage } from '../api/fn/news/get-public-homepage';
import { getRelatedArticles } from '../api/fn/news/get-related-articles';
import { listPublicArticles, ListPublicArticles$Params } from '../api/fn/news/list-public-articles';
import { listPublicTaxonomy } from '../api/fn/news/list-public-taxonomy';
import { PublicTaxonomyResponse } from '../api/models/public-taxonomy-response';

export const publicNewsRequests: HttpInterceptorFn = (request, next) => {
  if (request.method !== 'GET' || !request.url.includes('/api/news/')) return next(request);
  const viewerComments =
    request.url.endsWith('/comments') && isPlatformBrowser(inject(PLATFORM_ID));
  return next(
    request.clone({
      credentials: viewerComments ? 'same-origin' : 'omit',
      cache: 'no-store',
      transferCache: false,
    }),
  );
};

@Injectable({ providedIn: 'root' })
export class PublicNewsApi {
  private readonly http = inject(HttpClient);
  private readonly api = inject(Api);
  async homepage() {
    return (await firstValueFrom(getPublicHomepage(this.http, this.api.rootUrl))).body;
  }
  async list(params: ListPublicArticles$Params) {
    return (await firstValueFrom(listPublicArticles(this.http, this.api.rootUrl, params))).body;
  }
  async article(address: string) {
    return (await firstValueFrom(getPublicArticle(this.http, this.api.rootUrl, { address }))).body;
  }
  async related(id: string) {
    return (await firstValueFrom(getRelatedArticles(this.http, this.api.rootUrl, { id }))).body;
  }
  async taxonomy(kind: 'categories' | 'tags'): Promise<PublicTaxonomyResponse[]> {
    const items: PublicTaxonomyResponse[] = [];
    for (let page = 1; page <= 100000; page++) {
      const result = (
        await firstValueFrom(
          listPublicTaxonomy(this.http, this.api.rootUrl, { kind, page, pageSize: 100 }),
        )
      ).body;
      items.push(...result.items);
      if (!result.items.length || items.length >= Number(result.total)) break;
    }
    return items;
  }
}
