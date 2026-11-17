import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { newsError } from './news-api';

describe('Article validation feedback', () => {
  const failure = (errors: Record<string, string[]>) =>
    new HttpErrorResponse({ status: 400, error: { errors } });

  it('explains the affected field and its limit in Russian', () => {
    const message = newsError(
      failure({ summary: ['Use at most 500 characters without null characters.'] }),
    );
    expect(message).toContain('Краткое описание:');
    expect(message).toContain('500');
    expect(message).toContain('символов');
    expect(message).not.toContain('Use at most');
  });

  it('names each missing submission field and explains what to enter', () => {
    const message = newsError(
      failure({
        categoryId: ['Choose a category before submission.'],
        title: ['Provide a title before submission.'],
      }),
    );
    expect(message).toContain('Категория (Параметры): Выберите категорию');
    expect(message).toContain('Заголовок (Текст): Укажите заголовок');
    expect(message).not.toContain('Изменения не сохранены');
  });

  it('explains image alternative text and invalid links', () => {
    const message = newsError(
      failure({
        alt: ['Every image placement requires alternative text at submission.'],
        sources: ['Use absolute HTTP or HTTPS links without embedded credentials.'],
      }),
    );
    expect(message).toContain('альтернативный текст');
    expect(message).toContain('каждого изображения');
    expect(message).toContain('https://');
    expect(message).toContain('Источники (Параметры):');
  });

  it('identifies unsupported body formatting without exposing arbitrary server text', () => {
    expect(newsError(failure({ body: ['Use bold, italic and HTTP/HTTPS links only.'] }))).toContain(
      'Текст статьи (Текст): Поддерживаются жирный, курсив и ссылки',
    );
    const message = newsError(
      failure({ body: ['private debug detail'], unknownField: ['internal value'] }),
    );
    expect(message).toContain('Текст статьи (Текст):');
    expect(message).not.toContain('private debug detail');
    expect(message).not.toContain('internal value');
  });

  it('falls back safely for empty or malformed validation details', () => {
    expect(newsError(failure({}))).toContain('Не удалось');
    expect(
      newsError(new HttpErrorResponse({ status: 400, error: { errors: { body: null } } })),
    ).toContain('Текст статьи (Текст):');
  });
});
