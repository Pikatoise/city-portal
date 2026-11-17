import { Node } from '@tiptap/core';
import type { JSONContent } from '@tiptap/core';

export const blankBody = (): JSONContent => ({ type: 'doc', content: [{ type: 'paragraph' }] });
export const safeArticleUrl = (value: string): boolean => {
  try {
    const url = new URL(value);
    return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password;
  } catch {
    return false;
  }
};

export const ArticleMediaFigure = Node.create({
  name: 'mediaFigure',
  group: 'block',
  atom: true,
  addAttributes: () => ({
    mediaId: { default: '' },
    alt: { default: '' },
    caption: { default: '' },
  }),
  parseHTML: () => [],
  renderHTML: ({ HTMLAttributes }) => [
    'figure',
    { class: 'article-figure' },
    [
      'img',
      {
        src: `/api/media/images/${encodeURIComponent(HTMLAttributes['mediaId'])}/1280`,
        alt: HTMLAttributes['alt'],
      },
    ],
    ['figcaption', {}, HTMLAttributes['caption']],
  ],
});

export function renderArticleBody(doc: JSONContent): string {
  const escape = (text: unknown) =>
    String(text ?? '').replace(
      /[&<>"']/g,
      (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]!,
    );
  const render = (node: JSONContent): string => {
    if (node.type === 'text') {
      let text = escape(node.text);
      for (const mark of node.marks ?? []) {
        if (mark.type === 'bold') text = `<strong>${text}</strong>`;
        if (mark.type === 'italic') text = `<em>${text}</em>`;
        if (mark.type === 'link' && safeArticleUrl(mark.attrs?.['href'] ?? ''))
          text = `<a href="${escape(mark.attrs?.['href'])}" rel="noopener noreferrer">${text}</a>`;
      }
      return text;
    }
    if (node.type === 'mediaFigure')
      return `<figure><img src="/api/media/images/${encodeURIComponent(node.attrs?.['mediaId'] ?? '')}/1280" alt="${escape(node.attrs?.['alt'])}"><figcaption>${escape(node.attrs?.['caption'])}</figcaption></figure>`;
    const tag = (
      {
        doc: 'div',
        paragraph: 'p',
        heading: node.attrs?.['level'] === 3 ? 'h3' : 'h2',
        bulletList: 'ul',
        orderedList: 'ol',
        listItem: 'li',
        blockquote: 'blockquote',
        hardBreak: 'br',
      } as Record<string, string>
    )[node.type ?? ''];
    if (!tag) return '';
    const start = Number(node.attrs?.['start'] ?? 1);
    const attributes =
      tag === 'ol'
        ? ` start="${Number.isInteger(start) && start >= 1 && start <= 1000000 ? start : 1}"`
        : '';
    return tag === 'br'
      ? '<br>'
      : `<${tag}${attributes}>${(node.content ?? []).map(render).join('')}</${tag}>`;
  };
  return render(doc);
}
