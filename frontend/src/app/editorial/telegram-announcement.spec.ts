import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TelegramAnnouncementResponse } from '../api/models/telegram-announcement-response';
import { TelegramAnnouncement } from './telegram-announcement';
import { TelegramApi } from './telegram-api';

describe('Telegram announcement', () => {
  const api = { get: vi.fn(), queue: vi.fn(), suggest: vi.fn(), resolve: vi.fn() };
  const preview: TelegramAnnouncementResponse = {
    enabled: true,
    aiEnabled: true,
    channel: '@city_fixture',
    revisionId: 'public-revision',
    suggestedText: 'Public title\n\nPublic summary',
    articleUrl: 'https://city.example/news/public-article',
    delivery: null,
  };

  beforeEach(() => {
    vi.resetAllMocks();
    api.get.mockResolvedValue(preview);
    TestBed.configureTestingModule({ providers: [{ provide: TelegramApi, useValue: api }] });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('lets the editor use a suggestion and submit it only through the send action', async () => {
    const fixture = TestBed.createComponent(TelegramAnnouncement);
    fixture.componentRef.setInput('id', 'article-id');
    await fixture.whenStable();
    const panel = fixture.componentInstance;
    api.suggest.mockResolvedValue({ text: 'Suggested announcement' });
    panel.text = 'Unsaved manual text';

    await panel.generate();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Suggested announcement');
    expect(panel.text).toBe('Unsaved manual text');
    expect(api.queue).not.toHaveBeenCalled();
    panel.useSuggestion();
    api.queue.mockResolvedValue({ state: 'Queued', channel: '@city_fixture' });
    await panel.queue();
    await fixture.whenStable();
    expect(api.queue).toHaveBeenCalledWith(
      'article-id',
      'public-revision',
      '@city_fixture',
      'Suggested announcement',
    );
    expect(fixture.nativeElement.textContent).toContain('В очереди');
    expect(panel.canQueue()).toBe(false);
  });

  it('keeps the manually written announcement after provider failure', async () => {
    const fixture = TestBed.createComponent(TelegramAnnouncement);
    fixture.componentRef.setInput('id', 'article-id');
    await fixture.whenStable();
    const panel = fixture.componentInstance;
    panel.text = 'Manual announcement';
    api.suggest.mockRejectedValue(new HttpErrorResponse({ status: 503 }));

    await panel.generate();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain(
      'Можно отправить анонс вручную',
    );
    expect(panel.text).toBe('Manual announcement');
    expect(panel.canQueue()).toBe(true);
  });

  it('can reconcile a withdrawn article by linking the existing post', async () => {
    api.get.mockResolvedValue({
      enabled: true,
      aiEnabled: false,
      channel: '@current_city',
      delivery: { state: 'NeedsReview', channel: '@city_fixture', version: 4 },
    });
    const fixture = TestBed.createComponent(TelegramAnnouncement);
    fixture.componentRef.setInput('id', 'article-id');
    fixture.componentRef.setInput('channel', '@city_fixture');
    await fixture.whenStable();
    const panel = fixture.componentInstance;

    expect(api.get).toHaveBeenCalledWith('article-id', '@city_fixture');
    expect(panel.canRetry()).toBe(false);
    panel.postUrl = 'https://t.me/city_fixture/55';
    api.resolve.mockResolvedValue({
      state: 'Sent',
      channel: '@city_fixture',
      postUrl: 'https://t.me/city_fixture/55',
    });
    await panel.resolve('link');
    await fixture.whenStable();
    expect(api.resolve).toHaveBeenCalledWith(
      'article-id',
      expect.objectContaining({
        channel: '@city_fixture',
        version: 4,
        retry: false,
        postUrl: 'https://t.me/city_fixture/55',
      }),
    );
    expect(fixture.nativeElement.querySelector('a').href).toBe('https://t.me/city_fixture/55');
  });
});
