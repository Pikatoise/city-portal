# CityPortal UI Style

## Direction and Scope

Use calm editorial minimalism based on the owner-reviewed news homepage and article page. The content and images carry the hierarchy. Keep the established palette, fonts, orange logo dot, fluid public-page layout, and agreed navigation. Do not add fonts, decorative gradients, shadows, or boxed news cards. The existing lead-story image gradient is retained to make overlaid text readable.

Apply this language to public pages, account/recovery/installation forms, staff screens, and the article-editor prototype. This is visual consistency work, not acceptance of editor workflows or delivery of production News/Media. Product contracts remain in VALUE and the stage specifications.

## Shared Foundation

[design-tokens.css](../frontend/src/design-tokens.css) owns shared CSS variables and low-specificity control primitives. [styles.css](../frontend/src/styles.css) owns the application shell, account/staff forms, and utility classes. Component styles specialize layout and use these variables. Do not duplicate nearby shades for the same semantic role.

| Role | Value | Use |
| --- | --- | --- |
| Page | `#f7f9f6` | Main background and quiet form surfaces. |
| Surface | `#ffffff` | Inverse text and an explicitly raised surface where needed. |
| Muted surface | `#edf1e9` | Hover, selected state, and status support. |
| Text | `#233c31` | Headings and body text. |
| Secondary text | `#667364` | Dates, captions, hints, and metadata. |
| Action | `#294c38` | Primary actions, links, keyboard focus. |
| Action hover | `#3e6250` | Hover emphasis. |
| Control border | `#9eaf98` | Editable field and secondary-control outlines. |
| Divider | `#233c311a` | Quiet separation between content groups. |
| Brand dot | `#b37635` | Established logo accent. |
| Error | `#992d24` | Validation and request errors. |

Illustration colors are content, not additional interface palette roles. They may vary with the image.

## Typography and Density

- Retain the existing system font stack for navigation, headings, fields, body text, captions, and tables. Use the same body font in the article editor and its preview.
- Retain Georgia only for the prominent editorial-carousel headline. This is a deliberate display role, not an alternative application font.
- Public headlines retain their agreed responsive sizes. Account and staff page titles share a 30–44px scale. Form-section headings use 20px; form labels use 14px and medium emphasis.
- Reading/editing text uses 18px with 1.8 line-height; compact metadata uses 12–13px. Use tabular numerals in staff tables.
- Use natural headline wrapping as requested by the owner. Card titles/descriptions truncate to two lines; metadata truncates to one. Do not truncate the article body.
- Supporting labels use sentence case, without decorative uppercase or tracking.
- Use the 4/8/12/16/24/32/48px spacing family; controls can use 10px vertical and 12px horizontal padding. Keep each page's agreed content density.

## Layout and Components

- Public pages stay fluid. Header/category rows use 6vw side gutters, news uses 8vw, and mobile uses 5vw at the existing breakpoints. Article content retains its additional 7vw inset. Do not reintroduce a fixed public-page width cap.
- Narrow account forms keep their intentional field-width limits. Staff and authoring workspaces follow fluid page edges; data tables scroll inside their own container on small screens.
- Fields use the shared border, page background, 6px corner radius, and a minimum 42px height. Keep native inputs/selects and existing labels/validation.
- Primary actions use solid forest green with inverse text. Secondary actions use an outline or quiet transparent background. All actions have hover, disabled, and visible keyboard-focus states.
- News cards use subtle row dividers and no card shell. Images use 6px corners and no white letterboxing. Article media preserves intrinsic proportions; captions align with the figure.
- Editor panes use the page background and section rules, matching the reader instead of an unrelated white-card interface.
- Scope site chrome to `.site-header` and `.site-footer`. Generic `nav`/`footer` selectors must not change nested article metadata or internal navigation.

## Interaction and Accessibility

The account notification page follows the shared typography, buttons, muted metadata, and divider-based rows. Native fieldsets keep account and Email switches independently labelled, with visible unsaved/success/error feedback. Notification actions and settings fit desktop 1440px and mobile 390px without horizontal page overflow; the real HTTPS verification also covered read/delete and persistence after reload. The page is browser-rendered under `/account/notifications` and uses the existing account navigation.

Preserve the accepted sticky category row, 650ms interruptible category scrolling, 380ms carousel settling, and 180ms content entrance. These explicit owner-reviewed motions remain as configured; do not silently change them during theme work. The carousel retains arrow alternatives to dragging. Decorative image-hover animation and the loading spinner retain reduced-motion handling.

Every control needs a label and visible focus. The editor has a group focus outline instead of an invisible caret-only focus state. Logo dots are hidden from assistive technology. Text and controls must fit narrow screens; shared variables do not replace checking long content, zoom, and actual API states.

## Review Findings and Changes

| Owning file | Finding | Resolution |
| --- | --- | --- |
| `frontend/src/styles.css` | Similar colors and control rules diverged between account and staff screens. | Shared semantic palette, consistent field/action styling and title scale. |
| `frontend/src/styles.css` | Generic footer/navigation rules leaked into nested components. | Scoped application chrome; article footer explicitly owns its divider and spacing. |
| `frontend/src/app/news-prototype/news-prototype.css` | Writing and preview body fonts differed from the actual article reader. | Both use the existing system body font; editorial display font remains unchanged. |
| `frontend/src/app/news-prototype/news-prototype.css` | White editor panels and inconsistent borders competed with the quiet public pages. | Page-colored panes, shared dividers, matching controls, visible editor focus. |
| `frontend/src/app/news-prototype/news-prototype.html` | Fixture dates mixed raw ISO dates and localized presentation. | News card dates share the existing Russian date formatter. |
| Public component styles | Repeated hardcoded semantic colors encouraged drift. | Color and core gutter variables come from the shared foundation. |

Review reference: [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md). Owner decisions override generic suggestions, including natural headline wrapping and the explicitly requested page/scroll motion. The prototype's navigation buttons, memory-only editor state, and schema/media limitations remain prototype constraints; this style pass does not certify full guideline compliance or production accessibility.

## Authoring and Editorial Workspace

The owner delegated further editor design and implementation, with acceptance reserved for the final stage result instead of individual blocks. The current browser-only workspace is an implementation reference, not production authorization or persistence.

Design research used primary product documentation:

| Reference | Useful pattern | CityPortal adaptation |
| --- | --- | --- |
| [Ghost post settings](https://ghost.org/help/post-settings/) and [publishing](https://ghost.org/help/publishing-content/) | Separate writing, metadata preparation, preview, and publishing controls. | Quiet text canvas; explicit save/submit/publish actions and separate optional SEO controls. |
| [WordPress post settings](https://wordpress.org/documentation/article/page-post-settings-sidebar/) | Distinguish document-wide properties from content editing. | Images and parameters have their own navigation; authors need not keep every field beside the text. |
| [Sanity content operations](https://www.sanity.io/docs/user-guides/content-operations-cheatsheet) | Make review and retained history discoverable. | A contextual review panel and a history section with readable submitted snapshots. Advanced change comparison remains excluded. |

The selected composition uses the existing forest-green palette, system font, page-colored surfaces, restrained rules, and fluid gutters. The writing canvas receives most of the width. A smaller right column holds readiness or editorial decisions; it follows the document on mobile. The action bar stays visible on desktop. Five sections organize text, images, parameters, reader preview, and history. No new fonts, shadows, promotional text, or workflow permissions are introduced.

Authors have a searchable article list, incomplete drafts, memory-only five-second autosave, explicit save, submission, recall, resubmission notes, and separate publication. Images support a cover, a gallery, inline placement, contextual alternative text, and optional captions. The library demonstrates own files and approved common templates; it does not upload or moderate real files. Parameters contain category, tags, repeatable sources, attribution flag, and optional SEO overrides. Preview offers desktop/mobile reading widths without publishing.

Editors have a shared queue, claim/takeover, required feedback for return/rejection, approval, transfer/release, separate publication, and explicit published corrections/withdrawal. Review copies, private working content, and published correction buffers are separated in memory. The history control is absent for an unassigned editor. Native dialogs provide focus containment, Escape, and focus return for decisions and image selection. Demonstration persona controls live in a collapsed development section, outside the normal article actions.

Browser checks covered submission locking, return/resubmission with required change notes, approval/publication, direct corrections, withdrawal, and private unsent title isolation. All five sections and the image dialog fit a 390px viewport without page overflow. Both production SSR compilation and prototype compilation passed. The production API was unavailable during these checks; fixture persistence, role controls, and queue operations do not prove API permissions, durable history, concurrency, Media processing, or notification delivery.

## Earlier Theme Verification

On 2026-11-14, production SSR compilation and prototype compilation passed. Browser review covered the public homepage/article/editor at desktop and mobile widths. The homepage showcase remained within 1920×1080 and 1366×768 viewports; no horizontal page overflow was observed at 390px on reviewed public/editor screens.

Account login/registration/recovery/confirmation, profile, staff accounts/delivery screens, and the setup form were opened using the actual production browser bundle with synthetic API responses in an isolated browser session. Login, profile, accounts, delivery, and setup screens had no horizontal page overflow at 390px. This checks rendering, not actual account writes, permissions, Email delivery, backend integration, or SSR responses. Those workflows retain their existing acceptance requirements. No persistent browser tests or dependencies were added for this visual pass.
