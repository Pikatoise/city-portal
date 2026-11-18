import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountApi } from '../../account/account-api';
import { CityClock } from '../../staff/city-clock';
import { NewsApi } from '../news-api';
import { ReviewWorkspace } from './review-workspace';

describe('Editorial review workspace', () => {
  const api = {
    reviews: vi.fn(),
    review: vi.fn(),
    history: vi.fn(),
    act: vi.fn(),
    taxonomy: vi.fn(),
  };
  const account = { profile: vi.fn() };
  const review = {
    article: {
      id: 'story',
      version: 7,
      state: 'InReview',
      content: { title: 'Submitted title', gallery: [], sources: [] },
      previewHtml: '<p>Saved snapshot</p>',
    },
    authorId: 'author',
    reviewerId: 'editor',
    workingState: 'InReview',
    publicationState: 'NeverPublished',
    deleted: false,
  };
  beforeEach(() => {
    vi.resetAllMocks();
    account.profile.mockResolvedValue({ id: 'editor', roles: ['Editor'], emailConfirmed: true });
    api.reviews.mockResolvedValue({ items: [], total: 0 });
    api.review.mockResolvedValue(review);
    api.history.mockResolvedValue({ items: [], total: 0 });
    api.taxonomy.mockResolvedValue([]);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: NewsApi, useValue: api },
        { provide: AccountApi, useValue: account },
        { provide: CityClock, useValue: { format: (value: string) => value } },
      ],
    });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });
  it('renders the submitted snapshot and requires feedback for return and rejection', async () => {
    const fixture = TestBed.createComponent(ReviewWorkspace);
    const component = fixture.componentInstance;
    await component.ngOnInit();
    await component.open('story');
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.textContent).toContain('Submitted title');
    expect(host.textContent).toContain('Saved snapshot');
    const returned = [...host.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'На доработку',
    )!;
    expect(returned.disabled).toBe(true);
    const feedback = host.querySelector('textarea')!;
    feedback.value = 'Please verify the source';
    feedback.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(returned.disabled).toBe(false);
  });
  it('preserves feedback on a competing decision and clears private detail after release', async () => {
    const component = TestBed.createComponent(ReviewWorkspace).componentInstance;
    await component.ngOnInit();
    await component.open('story');
    component.reason = 'Keep this feedback';
    api.act.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await component.act('story', 7, 'return');
    expect(component.reason).toBe('Keep this feedback');
    expect(component.selected()?.article.content.title).toBe('Submitted title');
    expect(component.error()).not.toBe('');
    api.act.mockResolvedValueOnce({});
    await component.act('story', 7, 'release');
    expect(component.selected()).toBeNull();
    expect(component.history()).toEqual([]);
  });
  it('opens and focuses the submitted article preview after claiming a review', async () => {
    api.review.mockResolvedValue({
      ...review,
      article: {
        ...review.article,
        previewHtml:
          '<p>Saved snapshot</p><figure><img src="/api/media/images/body/1280" alt="Body photo"></figure>',
        content: {
          ...review.article.content,
          cover: { mediaId: 'cover', alt: 'Cover photo' },
          gallery: [{ mediaId: 'gallery', alt: 'Gallery photo' }],
        },
      },
    });
    api.reviews.mockResolvedValue({
      items: [{ id: 'story', title: 'Queued article', state: 'InReview', version: 6 }],
      total: 1,
    });
    api.act.mockResolvedValue({});
    const fixture = TestBed.createComponent(ReviewWorkspace);
    fixture.detectChanges();
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    const claimButton = () =>
      [...host.querySelectorAll('button')].find(
        (button) => button.textContent?.trim() === 'Взять на проверку',
      );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(claimButton()?.disabled).toBe(false);
    });
    claimButton()!.click();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(host.querySelector('.review-detail')?.textContent).toContain('Saved snapshot');
      expect(document.activeElement).toBe(host.querySelector('#selected-title'));
    });
    expect(
      [...host.querySelectorAll('.review-detail img')].map((image) => image.getAttribute('src')),
    ).toEqual([
      '/api/media/images/cover/1280',
      '/api/media/images/body/1280',
      '/api/media/images/gallery/1280',
    ]);
    expect(api.act).toHaveBeenCalledWith('story', 6, 'claim', '');
    expect(api.review).toHaveBeenCalledWith('story');
  });
  it('does not fetch the review queue for an author without editorial permissions', async () => {
    account.profile.mockResolvedValue({ id: 'author', roles: ['Author'], emailConfirmed: true });
    const component = TestBed.createComponent(ReviewWorkspace).componentInstance;
    await component.ngOnInit();
    expect(component.allowed()).toBe(false);
    expect(api.reviews).not.toHaveBeenCalled();
  });
});
