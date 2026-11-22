import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom, map } from 'rxjs';
import { StrictHttpResponse } from '../api/strict-http-response';
import { ApiWrite } from '../api-write';
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
import { correctPublishedArticle } from '../api/fn/editorial/correct-published-article';
import { createArticleWorkingVersion } from '../api/fn/editorial/create-article-working-version';
import { getEditorialPublication } from '../api/fn/editorial/get-editorial-publication';
import { listEditorialPublications } from '../api/fn/editorial/list-editorial-publications';
import { getEditorialHomepage } from '../api/fn/editorial/get-editorial-homepage';
import { saveEditorialHomepage } from '../api/fn/editorial/save-editorial-homepage';
import { setFeaturedArticle } from '../api/fn/editorial/set-featured-article';
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
  private readonly write = inject(ApiWrite);
  private request<T>(response: Observable<StrictHttpResponse<T>>): Promise<T> {
    return firstValueFrom(response.pipe(map((value) => value.body)));
  }
  list(page = 1, search = '') {
    return this.request(listOwnArticles(this.http, '', { page, pageSize: 20, search }));
  }
  get(id: string) {
    return this.request(getOwnArticle(this.http, '', { id }));
  }
  create() {
    return this.write.invoke(createArticleDraft, {});
  }
  save(id: string, version: number | string, content: ArticleContent) {
    return this.write.invoke(saveArticleDraft, { id, body: { version, content } });
  }
  delete(id: string, version: number | string) {
    return this.write.invoke(deleteArticleDraft, { id, version });
  }
  act(id: string, version: number | string, operation: string, reason = '') {
    if (operation === 'working-version')
      return this.write.invoke(createArticleWorkingVersion, { id, body: { version } });
    return this.write.invoke(actOnArticleReview, { id, operation, body: { version, reason } });
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
  publication(id: string) {
    return this.request(getEditorialPublication(this.http, '', { id }));
  }
  publications(page = 1) {
    return this.request(listEditorialPublications(this.http, '', { page, pageSize: 20 }));
  }
  homepage() {
    return this.request(getEditorialHomepage(this.http, ''));
  }
  saveHomepage(version: number | string, firstCategoryId: string, secondCategoryId: string) {
    return this.write.invoke(saveEditorialHomepage, {
      body: { version, firstCategoryId, secondCategoryId },
    });
  }
  feature(id: string, version: number | string, featured: boolean) {
    return this.write.invoke(setFeaturedArticle, { id, body: { version, featured } });
  }
  correct(id: string, version: number | string, content: ArticleContent, reason: string) {
    return this.write.invoke(correctPublishedArticle, { id, body: { version, content, reason } });
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
    return this.write.invoke(createEditorialTaxonomy, { kind, body: { name } });
  }
  updateTaxonomy(entry: TaxonomyResponse, name: string, archived: boolean) {
    return this.write.invoke(saveEditorialTaxonomy, {
      kind: entry.kind,
      id: entry.id,
      body: { name, archived, version: entry.version },
    });
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
      if (
        fields &&
        typeof fields === 'object' &&
        !Array.isArray(fields) &&
        Object.keys(fields).length
      ) {
        const labels: Record<string, string> = {
          title: 'Заголовок (Текст)',
          summary: 'Краткое описание',
          body: 'Текст статьи (Текст)',
          bodyAlt: 'Изображения в тексте (Текст)',
          images: 'Изображения',
          categoryId: 'Категория (Параметры)',
          tagIds: 'Теги (Параметры)',
          taxonomy: 'Категория и теги (Параметры)',
          categories: 'Категории главной страницы',
          featured: 'Избранные статьи',
          sources: 'Источники (Параметры)',
          alt: 'Альтернативный текст изображений',
          caption: 'Подпись изображения',
          seoTitle: 'SEO-заголовок (Параметры)',
          seoDescription: 'SEO-описание (Параметры)',
          link: 'Ссылка в тексте',
          name: 'Название',
          content: 'Поля статьи',
          submission: 'Обязательные поля',
          reason: 'Описание изменений или причина решения',
          page: 'Номер страницы',
        };
        return Object.entries(fields)
          .map(([field, messages]) => {
            const reasons = Array.isArray(messages) && messages.length ? messages : [null];
            return `${labels[field] ?? 'Данные'}: ${reasons.map(articleValidationReason).join(' ')}`;
          })
          .join(' ');
      }
    }
  }
  return errorMessage(error);
}

function articleValidationReason(value: unknown): string {
  const messages: Record<string, string> = {
    'Provide a title before submission.': 'Укажите заголовок перед отправкой на проверку.',
    'Provide a summary before submission.':
      'Добавьте краткое описание перед отправкой на проверку.',
    'Choose a category before submission.': 'Выберите категорию перед отправкой на проверку.',
    'Provide article text before submission.': 'Добавьте текст статьи перед отправкой на проверку.',
    'Provide a name and URL for each source; attribution requires at least one source.':
      'Укажите название и ссылку каждого источника. Если используются чужие материалы, добавьте хотя бы один источник.',
    'Every image placement requires alternative text at submission.':
      'Заполните альтернативный текст для каждого изображения: обложки, галереи и изображений в тексте.',
    'Every image in the article body requires alternative text before submission.':
      'Выберите каждое изображение в тексте и заполните его альтернативный текст.',
    'Use absolute HTTP or HTTPS links without embedded credentials.':
      'Укажите полную ссылку с http:// или https://, без логина и пароля в адресе.',
    'Provide an absolute HTTP/HTTPS link.': 'Укажите полную ссылку с http:// или https://.',
    'Provide valid Unicode text.': 'Удалите повреждённые символы или введите текст заново.',
    'Use a version 1 structured document of up to 2 MiB.':
      'Размер текста с форматированием не должен превышать 2 МиБ. Сократите статью.',
    'The body must not exceed 100000 plain-text characters.':
      'Сократите текст статьи до 100 000 символов.',
    'The document is too deeply nested or contains too many nodes.':
      'Упростите вложенные списки и цитаты: не более 24 уровней и 20 000 элементов.',
    'Use only the supported document blocks and nesting (up to 2000 blocks).':
      'Используйте абзацы, заголовки H2/H3, списки, цитаты и изображения; не более 2 000 блоков. Проверьте вложенность списков.',
    'Use supported text marks only.':
      'Уберите лишнее форматирование: доступны жирный, курсив и ссылки.',
    'Use bold, italic and HTTP/HTTPS links only.':
      'Поддерживаются жирный, курсив и ссылки http:// или https://. Уберите другое форматирование.',
    'Use H2 or H3 subheadings.': 'Выберите для подзаголовка уровень H2 или H3.',
    'Use a valid ordered-list start.': 'Начальный номер списка должен быть от 1 до 1 000 000.',
    'Lists and quotations require valid child blocks.':
      'Добавьте текст в пустой список или цитату либо удалите этот блок.',
    'Text nodes must not be empty.':
      'Удалите пустой фрагмент форматированного текста и введите его заново.',
    'Text nodes cannot contain attributes or children.':
      'Фрагмент текста содержит неподдерживаемое форматирование. Уберите форматирование этого фрагмента.',
    'Only text nodes may contain text or marks.':
      'Блок содержит неподдерживаемое форматирование. Уберите его или введите фрагмент заново.',
    'Only text nodes may contain text.':
      'Перенос строки содержит недопустимый текст. Удалите перенос и вставьте его заново.',
    'Unsupported document property or attribute.':
      'Обнаружено неподдерживаемое форматирование. Уберите его или вставьте фрагмент как обычный текст.',
    'Block content must be an array.':
      'Повреждена структура блока. Удалите этот блок и введите его заново.',
    'This node cannot have children.':
      'Повреждена структура изображения или переноса строки. Вставьте этот элемент заново.',
    'Provide an integer attribute.':
      'Проверьте уровень заголовка и начальный номер списка: требуются целые числа.',
    'Use at most ten tags and twenty sources.':
      'Выберите не более 10 тегов и добавьте не более 20 источников.',
    'Use at most twenty distinct images and valid taxonomy IDs.':
      'Используйте не более 20 разных изображений и выберите категорию и теги из списка.',
    'Provide valid source entries.': 'Для каждого источника укажите название и полную ссылку.',
    'Provide a valid Media ID.': 'Выберите готовое изображение через окно выбора изображений.',
    'Use a valid Media ID.': 'Выберите готовое изображение через окно выбора изображений.',
    'Select existing active taxonomy; only existing associations may retain archived entries.':
      'Выберите действующую категорию и теги из списка. Архивные значения можно сохранить только у уже связанной с ними статьи.',
    'Provide a category or tag name.': 'Введите название категории или тега.',
    'Describe changes before resubmitting.':
      'Заполните описание изменений перед повторной отправкой.',
    'An editorial correction reason is required.': 'Укажите причину редакторского исправления.',
    'This public address is already in use. Change the title.':
      'Такой адрес уже занят. Измените заголовок.',
    'A withdrawal reason is required.': 'Укажите причину снятия с публикации.',
    'Feedback or a rejection reason is required.': 'Укажите замечания или причину отклонения.',
    'Select two distinct active categories in display order.':
      'Выберите две разные действующие категории в нужном порядке.',
    'At most five articles can be selected.': 'Выберите не более пяти избранных статей.',
  };
  if (typeof value !== 'string') return 'Проверьте значение этого поля.';
  const maximum = /^Use at most (\d+) characters without null characters\.$/.exec(value);
  if (maximum)
    return `Не более ${maximum[1]} символов. Удалите лишний текст и недопустимые нулевые символы.`;
  return messages[value] ?? 'Проверьте значение этого поля.';
}
