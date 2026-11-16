import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountApi } from '../account/account-api';
import { MediaApi } from '../media/media-api';
import { CityClock } from '../staff/city-clock';
import { AuthorWorkspace } from './author-workspace';
import { NewsApi, ArticleResponse } from './news-api';
import { renderArticleBody } from './article-document';

const article: ArticleResponse = {
  id: 'article-1',
  revisionId: 'revision-1',
  version: 1,
  state: 'Draft',
  updatedAtUtc: '2026-11-15T10:00:00Z',
  previewHtml: '<p></p>',
  content: {
    title: '',
    summary: '',
    schemaVersion: 1,
    body: { type: 'doc', content: [{ type: 'paragraph' }] },
    tagIds: [],
    gallery: [],
    sources: [],
  },
};

describe('Author draft persistence', () => {
  const api = {
    get: vi.fn(),
    list: vi.fn(),
    create: vi.fn(),
    save: vi.fn(),
    delete: vi.fn(),
    taxonomy: vi.fn(),
    act: vi.fn(),
    history: vi.fn(),
    publication: vi.fn(),
    correct: vi.fn(),
  };
  const account = { profile: vi.fn() };
  beforeEach(() => {
    vi.useFakeTimers();
    vi.resetAllMocks();
    api.get.mockResolvedValue(structuredClone(article));
    api.create.mockResolvedValue(structuredClone(article));
    api.list.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 });
    api.taxonomy.mockResolvedValue([]);
    account.profile.mockResolvedValue({
      displayName: 'Author',
      emailConfirmed: true,
      roles: ['Author'],
    });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: NewsApi, useValue: api },
        { provide: AccountApi, useValue: account },
        { provide: MediaApi, useValue: {} },
        { provide: CityClock, useValue: { format: (value: string) => value } },
      ],
    });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('renders the Russian workspace and real editor labels', async () => {
    const fixture = TestBed.createComponent(AuthorWorkspace);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Мои статьи');
    await fixture.componentInstance.create();
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('textarea[name=title]').closest('label').textContent,
    ).toContain('Заголовок');
    expect(
      fixture.nativeElement.querySelector('[role=textbox][aria-label="Текст статьи"]'),
    ).not.toBeNull();
  });

  async function workspace() {
    const fixture = TestBed.createComponent(AuthorWorkspace);
    const component = fixture.componentInstance;
    await component.initialize();
    await component.create();
    return component;
  }

  it('disables approved autosave and explicitly saves a new reviewed version', async () => {
    const component = await workspace();
    api.get.mockResolvedValue({ ...article, state: 'Approved', previouslySubmitted: true });
    await component.open(article.id);
    component.form().title = 'Changed approved title';
    component.changed();
    await vi.advanceTimersByTimeAsync(10000);
    expect(api.save).not.toHaveBeenCalled();
    api.save.mockResolvedValue({
      ...article,
      version: 2,
      state: 'InReview',
      content: { ...article.content, title: 'Changed approved title' },
    });
    await component.save();
    expect(api.save).toHaveBeenCalledOnce();
    expect(component.article()?.state).toBe('InReview');
    expect(component.editable()).toBe(false);
    expect(component.dirty()).toBe(false);
    expect(api.act).not.toHaveBeenCalled();
    component.ngOnDestroy();
  });

  it('does not submit when the required draft save fails', async () => {
    const component = await workspace();
    component.form().title = 'Keep this text';
    component.changed();
    api.save.mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await component.workflow('submit');
    expect(component.form().title).toBe('Keep this text');
    expect(component.dirty()).toBe(true);
    expect(api.act).not.toHaveBeenCalled();
    component.ngOnDestroy();
  });

  it('retries resubmission after correcting the change description without editing saved content', async () => {
    const component = await workspace();
    api.act.mockRejectedValueOnce(new HttpErrorResponse({ status: 400 }));
    await component.workflow('submit');
    expect(component.error()).not.toBe('');
    component.changeDescription = 'Added the missing attribution';
    api.act.mockResolvedValueOnce({ ...article, version: 2, state: 'InReview' });
    await component.workflow('submit');
    expect(api.act).toHaveBeenCalledTimes(2);
    expect(api.act).toHaveBeenLastCalledWith(
      article.id,
      1,
      'submit',
      'Added the missing attribution',
    );
    expect(component.article()?.state).toBe('InReview');
    expect(component.error()).toBe('');
    component.ngOnDestroy();
  });

  it('keeps review content read-only and recalls it through a versioned action', async () => {
    const component = await workspace();
    api.get.mockResolvedValue({ ...article, state: 'InReview', version: 4 });
    await component.open(article.id);
    expect(component.editable()).toBe(false);
    await component.save();
    expect(api.save).not.toHaveBeenCalled();
    api.act.mockResolvedValue({ ...article, version: 5 });
    await component.workflow('recall');
    expect(api.act).toHaveBeenCalledWith(article.id, 4, 'recall', '');
    expect(component.editable()).toBe(true);
    component.ngOnDestroy();
  });

  it('waits five idle seconds, serializes saves and preserves edits made during the first request', async () => {
    const component = await workspace();
    let finish: (value: ArticleResponse) => void = () => {};
    api.save.mockImplementationOnce(
      () =>
        new Promise<ArticleResponse>((resolve) => {
          finish = resolve;
        }),
    );
    component.form().title = 'First edit';
    component.changed();
    await vi.advanceTimersByTimeAsync(4999);
    expect(api.save).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(api.save).toHaveBeenCalledWith(
      'article-1',
      1,
      expect.objectContaining({ title: 'First edit' }),
    );
    component.form().title = 'Newer edit';
    component.changed();
    await vi.advanceTimersByTimeAsync(5000);
    expect(api.save).toHaveBeenCalledTimes(1);
    expect(component.dirty()).toBe(true);
    api.save.mockResolvedValueOnce({
      ...article,
      version: 3,
      content: { ...article.content, title: 'Newer edit' },
    });
    finish({ ...article, version: 2, content: { ...article.content, title: 'First edit' } });
    await vi.advanceTimersByTimeAsync(0);
    expect(api.save).toHaveBeenCalledTimes(2);
    expect(api.save).toHaveBeenLastCalledWith(
      'article-1',
      2,
      expect.objectContaining({ title: 'Newer edit' }),
    );
    expect(component.form().title).toBe('Newer edit');
    expect(component.dirty()).toBe(false);
    component.ngOnDestroy();
  });

  it('stops autosave on conflict and exposes a complete local copy without replacing the form', async () => {
    const component = await workspace();
    component.form().title = 'Unsaved text';
    component.changed();
    api.save.mockRejectedValue(new HttpErrorResponse({ status: 409 }));
    await component.save();
    expect(component.conflict()).toBe(true);
    expect(component.form().title).toBe('Unsaved text');
    expect(JSON.parse(component.copyBuffer()).title).toBe('Unsaved text');
    component.form().summary = 'Another change';
    component.changed();
    await vi.advanceTimersByTimeAsync(30000);
    expect(api.save).toHaveBeenCalledTimes(1);
    expect(component.dirty()).toBe(true);
    component.ngOnDestroy();
  });

  it('retains failed network saves for an explicit retry and never reports them as saved', async () => {
    const component = await workspace();
    component.form().title = 'Offline edit';
    component.changed();
    api.save.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.save();
    expect(component.dirty()).toBe(true);
    expect(component.notice()).toBe('');
    expect(component.error()).not.toBe('');
    await vi.advanceTimersByTimeAsync(10000);
    expect(api.save).toHaveBeenCalledTimes(1);
    api.save.mockResolvedValueOnce({
      ...article,
      version: 2,
      content: { ...article.content, title: 'Offline edit' },
    });
    await component.save();
    expect(component.dirty()).toBe(false);
    expect(component.notice()).toBe('Черновик сохранён.');
    component.ngOnDestroy();
  });

  it('keeps edits typed while a conflict reload is in flight', async () => {
    const component = await workspace();
    component.form().title = 'Local';
    component.changed();
    component.conflict.set(true);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    let finish: (value: ArticleResponse) => void = () => {};
    api.get.mockImplementationOnce(
      () =>
        new Promise<ArticleResponse>((resolve) => {
          finish = resolve;
        }),
    );
    const reloading = component.reloadCurrent();
    component.form().title = 'Typed during reload';
    component.changed();
    finish({ ...article, version: 8, content: { ...article.content, title: 'Server version' } });
    await reloading;
    expect(component.form().title).toBe('Typed during reload');
    expect(component.dirty()).toBe(true);
    expect(component.conflict()).toBe(true);
    expect(JSON.parse(component.copyBuffer()).title).toBe('Typed during reload');
    component.ngOnDestroy();
  });

  it('does not request private article data without a current confirmed Author role', async () => {
    account.profile.mockResolvedValue({
      displayName: 'Editor',
      emailConfirmed: true,
      roles: ['Administrator'],
    });
    const component = TestBed.createComponent(AuthorWorkspace).componentInstance;
    await component.initialize();
    expect(component.allowed()).toBe(false);
    expect(api.list).not.toHaveBeenCalled();
    expect(api.taxonomy).not.toHaveBeenCalled();
    component.ngOnDestroy();
  });

  it('creates a separate published proposal and allows draft autosave without deleting withdrawn material', async () => {
    const fixture = TestBed.createComponent(AuthorWorkspace);
    const component = fixture.componentInstance;
    await component.initialize();
    api.get.mockResolvedValue({ ...article, state: 'Published', publicationState: 'Published' });
    await component.open(article.id);
    expect(component.editable()).toBe(false);
    api.act.mockResolvedValue({ ...article, version: 2, publicationState: 'Published' });
    await component.workflow('working-version');
    expect(api.act).toHaveBeenCalledWith(article.id, 1, 'working-version', '');
    expect(component.editable()).toBe(true);
    component.form().title = 'Private replacement';
    component.changed();
    api.save.mockResolvedValue({ ...article, version: 3, publicationState: 'Published' });
    await vi.advanceTimersByTimeAsync(5000);
    expect(api.save).toHaveBeenCalledWith(
      article.id,
      2,
      expect.objectContaining({ title: 'Private replacement' }),
    );
    component.article.set({ ...article, publicationState: 'Withdrawn' });
    fixture.detectChanges();
    const deleteButton = [...fixture.nativeElement.querySelectorAll('button')].find(
      (button: unknown) => (button as HTMLButtonElement).textContent?.includes('Удалить'),
    ) as HTMLButtonElement;
    expect(deleteButton?.disabled ?? true).toBe(true);
    component.ngOnDestroy();
  });

  it('requires an explicit reasoned correction save without autosave or author-only API requests', async () => {
    TestBed.overrideProvider(ActivatedRoute, {
      useValue: {
        snapshot: { data: { correction: true }, paramMap: { get: () => article.id } },
      },
    });
    account.profile.mockResolvedValue({
      displayName: 'Editor',
      emailConfirmed: true,
      roles: ['Editor'],
    });
    api.publication.mockResolvedValue({
      ...article,
      state: 'Published',
      publicationState: 'Published',
    });
    const fixture = TestBed.createComponent(AuthorWorkspace);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    const component = fixture.componentInstance;
    expect(component.allowed()).toBe(true);
    expect(api.list).not.toHaveBeenCalled();
    expect(api.get).not.toHaveBeenCalled();
    component.form().title = 'Editor correction';
    component.changed();
    await vi.advanceTimersByTimeAsync(6000);
    expect(api.correct).not.toHaveBeenCalled();
    await component.save();
    expect(component.error()).toContain('причину');
    component.changeDescription = 'Verified correction';
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    api.correct.mockResolvedValue({
      ...article,
      version: 2,
      state: 'Published',
      publicationState: 'Published',
    });
    await component.save();
    expect(api.correct).toHaveBeenCalledWith(
      article.id,
      1,
      expect.objectContaining({ title: 'Editor correction' }),
      'Verified correction',
    );
    expect(api.save).not.toHaveBeenCalled();
    expect(component.notice()).toContain('Исправления опубликованы');
    component.ngOnDestroy();
  });
});

describe('Article preview', () => {
  it('preserves bounded ordered-list starts and escapes text and image captions', () => {
    const html = renderArticleBody({
      type: 'doc',
      content: [
        {
          type: 'orderedList',
          attrs: { start: 3 },
          content: [
            {
              type: 'listItem',
              content: [
                { type: 'paragraph', content: [{ type: 'text', text: '<script>unsafe</script>' }] },
              ],
            },
          ],
        },
        {
          type: 'mediaFigure',
          attrs: { mediaId: 'image-id', alt: 'River', caption: '<img src=x>' },
        },
      ],
    });
    expect(html).toContain('<ol start="3">');
    expect(html).toContain('&lt;script&gt;');
    expect(html).not.toContain('<script>');
    expect(html).toContain('&lt;img src=x&gt;');
  });
});
