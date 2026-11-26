# CityPortal UI Style

## Direction and Scope

Use calm editorial minimalism based on the owner-reviewed news homepage and article page. The content and images carry the hierarchy. Keep the established palette, fonts, orange logo dot, fluid public-page layout, and agreed navigation. Do not add fonts, decorative gradients, shadows, or boxed news cards. The existing lead-story image gradient is retained to make overlaid text readable.

Apply this language to public pages, account/recovery/installation forms, staff screens, and the production article editor. Product contracts remain in VALUE and the stage specifications.

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

Use shared native controls in the authoring, taxonomy, and editorial
workspaces. Keep the editor's Russian labels. Use shared typography,
buttons, muted metadata, and divider-based rows for account notifications.
Use native fieldsets to label account and Email switches independently.
Show unsaved, success, and error states.

Preserve the accepted sticky category row, 650ms interruptible category scrolling, 380ms carousel settling, and 180ms content entrance. These explicit owner-reviewed motions remain as configured; do not silently change them during theme work. The carousel retains arrow alternatives to dragging. Decorative image-hover animation and the loading spinner retain reduced-motion handling.

Every control needs a label and visible focus. The editor has a group focus outline instead of an invisible caret-only focus state. Logo dots are hidden from assistive technology. Text and controls must fit narrow screens; shared variables do not replace checking long content, zoom, and actual API states.

## Authoring Workspace

The production workspace uses five sections: text, images, parameters, reader preview
and history. The writing canvas takes most desktop width; a smaller right column
holds readiness/review actions and follows the document on mobile. The action bar
stays visible on desktop. Images support contextual alternative text and captions;
parameters hold taxonomy, attribution, sources and optional SEO. Preview offers
desktop/mobile widths without publication. Native decision/image dialogs retain
focus containment, Escape and focus return.

## Editorial Review and Publication

Keep the five authoring sections visible across content states. Show
read-only content for submitted, rejected, and published versions.
In the approved state, explain what explicit Save does. Preserve the local
form when it contains unsaved changes or a version conflict occurs.

The reviewer workspace uses shared controls and focuses the submitted preview
after a claim or takeover. The preview displays the body and its photos,
the cover, the gallery, sources, and SEO. Failed image reads show placeholders.
After a return, the workspace shows the preceding submitted snapshot and
explains who can see the author's current edits.

## Public News

Use the shared public gutters for the homepage, detailed list, and article.
Use safe Media URLs for featured images and show explicit no-cover and
unavailable-image states. Expose one active carousel slide to assistive
technology and keep the author and date on one line. Category scrolling
stops on wheel, touch, or keyboard input. Clean up its animation frames and
listeners when the component is destroyed.

Use labelled filters and numbered pages on the detailed list. Show the
sticky category bar only on the homepage. Editorial curation controls
preserve input and show feedback on version conflicts and request failures.
