# CityPortal — Product Vision and Value

## Purpose

CityPortal is a portfolio and learning project for professional .NET development. It combines public news, classified advertisements, citizen story suggestions, and editorial and administrative workspaces. Development and demonstrations use a fictional city; adoption by real residents is not a project goal. The learning goal is to understand how complete systems are designed and built.

Technical architecture, engineering requirements, tooling, working method, delivery order, and documentation planning are described in [CORE.md](CORE.md).

## Product Scope

News is the central capability; classifieds complement it. Official articles are written by authors and reviewed by editors. Residents can supply topics and raw materials for authors.

Public news, advertisements, and comments are readable without an account. Anonymous visitors can comment and report advertisements. Other content creation and personal account actions require authentication.

Each installation serves one city. Configurable settings include portal name, branding, city and region, time zone, coordinates, districts and locations, contacts, SEO metadata, and external services. Changing city settings must not reinterpret or migrate existing content.

### First-Run Installation

A new installation provides a one-time wizard for site name, city, optional region, time zone, first administrator's Email/display name/password, and SMTP settings with a test message. Email remains the login. The first administrator has immediate access without confirmation; ordinary registration keeps its confirmation rules.

The owner completes setup with access restricted to the server or an SSH tunnel, then opens network access. No installation key is required. Until setup succeeds, only the wizard is available and normal navigation is hidden. Settings and administrator creation are atomic and survive restarts. Successful setup closes the wizard permanently; existing account databases cannot be taken over through it.

Later settings editing, branding, SEO, and integration fields arrive with their owning workflows. Detailed installation contracts belong in [STAGE_1.md](stages/STAGE_1.md#first-run-setup-contract).

## Users and Accounts

| User or role | Main responsibility |
| --- | --- |
| Visitor | Public access without an account. |
| Regular user | Personal content and account management. |
| Author | Prepare articles and work with citizen suggestions. |
| Editor | Review and publish articles, correct published content, and curate news. |
| Moderator | Review shared image templates, moderate advertisements and comments, handle reports, and restrict ordinary users. |
| Administrator | Manage accounts, roles, portal settings, integrations, and administrative operations. |

Accounts are unified and may hold multiple roles. Access depends on role, ownership, and content state.

Administrators automatically have all Editor permissions without a separately assigned Editor role. This does not grant access to another author's private drafts before submission.

### Registration and Access

- Registration and login use Email and password, with a separate display name.
- Email confirmation is required before creating advertisements, story suggestions, or articles.
- Password recovery uses a one-time link sent by Email.
- Staff register as ordinary users; an administrator assigns author, editor, or moderator roles. The same account retains personal and work capabilities.
- Every registered account retains the Regular User role. Administrators can also grant and revoke the Administrator role and change their own staff roles, but the last administrator cannot lose administrative access. Role changes retain their actor, target, time, and old/new roles without automatic history deletion.
- Unconfirmed users can log in, view their own profile, resend confirmation from that profile, and log out. Staff operations require confirmed Email. Password recovery remains available without login; invalid or expired confirmation-link pages only explain the result, with replacement confirmation requests available in the profile.
- If the Email service is unavailable, registration may still create an unconfirmed account. User-facing Email delivery states are "Отправлено" and "Ошибка, повторите письмо". "Отправлено" means acceptance by the outgoing mail server, not verified inbox delivery. Show a loading indicator without a separate status during the initial attempt. After a failed attempt, show "Ошибка, повторите письмо" while background retries continue; change it to "Отправлено" if a later attempt succeeds. Confirmation remains required for the actions listed above.

Detailed account constraints, link and session lifetimes, abuse limits, administration screens, and acceptance contracts are recorded in [STAGE_1.md](stages/STAGE_1.md). New same-purpose links replace previous links, and changing or resetting a password ends all existing sessions.

### Account Blocking

- Moderators can block and unblock only ordinary users; administrators can block and unblock any user, including staff.
- Blocks require a reason and remain until manually removed. History records the reason and responsible staff member.
- Successful blocking prevents subsequent account actions and immediately hides all its advertisements, including direct advertisement pages. If blocking and hiding cannot both complete, the operation fails without partially applying either change. Published articles remain available unless separately withdrawn by an editor.
- After unblocking, advertisements remain hidden until the owner manually relists or renews each one. Direct advertisement blocks still require the correction and approval process described below.

## Public Website

### Navigation and Homepage

Navigation includes Home, News, Classifieds, Submit News, User Account, Login and Registration, About, Contacts, and Terms and Rules.

The Stage 2 homepage presents editor-selected featured news as its main block and a general feed of all latest news, with categories and search. Editors and administrators select up to five featured articles for the large card. Show one at a time, with previous/next arrows only when more than one is selected. Keep first-publication order, newest first, and immediately remove withdrawn articles. Three small cards alongside are the latest articles from the general news feed, not editorial selections; the remaining general news continues below as wide rows. Recent advertisements and access to story suggestions and advertisement creation arrive with their owning stages.

### News and Article Content

The news section provides a chronological feed, categories and tags, search and filters, featured stories, and article pages. Article pages show author information, publication and update times, available media and sources, related articles, and search-friendly URLs and metadata.

Show the author's current account display name in all articles; account name changes update the displayed credit without changing article authorship. Articles have optional separate SEO title and description. When absent, use the article title and summary; the cover supplies the link-preview image when present. SEO overrides belong to the article revision and follow the same review/publication rules as other content.

Without a search query, sort news by first publication time, newest first; later corrections do not move an article to the top. Search covers title, summary, and body, accounts for Russian word forms, and sorts by relevance. Search only current published revisions. Filters include category, one tag, and a first-publication date range, combinable with search. News lists use numbered pages of 20 articles.

The current URL and previous URLs of a withdrawn article display "Материал снят с публикации" without article content or the withdrawal reason. Never-published content remains unavailable to anonymous readers.

Show up to five related published articles: first those sharing tags, then others in the same category; within each group, newer first. Exclude the current article and duplicate results.

Submission requires a title, short summary, body, and one category. The author comes from the account. Cover images, galleries, and tags are optional; sources are required when using material from others. The system checks required fields, and the editor assesses the content.

Sources are a separate list of source names and URLs. The author marks whether others' materials were used; when marked, submission requires at least one source. Editors assess attribution accuracy.

At submission, the title contains 1-200 characters, the summary 1-500, and the body 1-100,000 plain-text characters excluding markup. A version may have at most ten tags. Drafts may omit required fields, but supplied content remains subject to the maximum limits.

When a published title changes, update the article URL and redirect previous article URLs to the current one. Pending title changes do not alter the public URL before publication.

The visual editor supports paragraphs, subheadings, emphasis, lists, links, quotations, and images with captions. It does not support arbitrary HTML or complex page layout.

Image captions are optional. Each image placement in an article, including the cover and gallery, requires alternative text describing the image; the same image may have different alternative text in different contexts.

Editors and administrators create, rename, and archive categories and tags. Stage 2 categories form one flat list without subcategories; each article has one category. Authors select existing entries. Category names and tag names are unique separately, ignoring case and outer whitespace; archived entries retain their names in the uniqueness constraint. Archived entries cannot be selected for new associations; existing associations remain valid when submitting and publishing new article versions.

### Editorial Workflow

`Draft -> In Review -> Approved -> Published`

Before submission, a draft is visible only to its author, including when another user is an editor or administrator. Submission makes the article available to the editorial review workflow. After a return for corrections, new unsent edits remain visible only to the author until resubmission; the previously submitted version and its history remain available under the agreed review permissions.

Drafts support server-side autosave and an explicit Save button. Incomplete drafts may omit title, category, or body; required submission fields are checked when sending the article for review. Saving checks the record version to prevent one tab from overwriting another tab's changes.

Authors can delete their own articles at any stage before first publication, including after submission, return, or final rejection. An article that has ever been published cannot be deleted through this author operation. Deletion removes the article from the author workspace and editorial queues while retaining the full text of every version and the action/review history for editorial access. After deletion, retained versions/history are accessible only to the assigned editor and administrators if the article was submitted. Never-submitted drafts remain closed to editors and administrators after deletion.

Removing the Author role closes the author workspace and its API operations entirely, including access to the account's own unpublished articles and history, unless another current role grants the relevant permission. Article ownership alone does not preserve author access. Public reading remains available.

1. The author prepares a publication-ready article and submits it. Author changes are blocked during review.
2. An editor approves it, returns it with required feedback, or finally rejects it with a reason.
3. On return, the author corrects the text, briefly describes the changes, and resubmits to the previous reviewer if that reviewer still has effective editorial permissions; otherwise the article returns to the shared queue. This repeats until approval or final rejection.
4. Any editor or the article's author can publish the approved version. The author does not need an editor role for this action.

An author may withdraw a submission for corrections, ending its active review. On resubmission, route it to the previous reviewer if that reviewer still has effective editorial permissions; otherwise use the shared queue. Final rejection closes the article's consideration: the author can retain the text or delete the never-published article, but cannot resubmit that article. Approved but unpublished versions are edited without autosave: an explicit Save validates the required submission fields, invalidates approval, and immediately sends the changed version to review. No separate submission action is needed. Route it to the previous reviewer if effective editorial permissions remain, otherwise to the shared queue.

Submitted articles enter a shared review queue. An editor takes an article from the queue and becomes its assigned reviewer. The assigned editor may release the review back to the shared queue, with the release recorded in history. Another editor may explicitly take over, with the transfer recorded in history. Approval and publication are recorded as separate actions with their responsible users. If a reviewer loses all effective editorial permissions, their assigned reviews return to the shared queue with a history record. Losing Editor alone does not release reviews while Administrator still grants editorial permissions.

After first submission, article history is accessible to the author, the assigned editor, and administrators, subject to current effective permissions. History is not public or available to all editors merely by role. The assigned editor retains history access after completing the review while they retain effective editorial permissions. Explicit takeover or release to the shared queue revokes the previous editor's access; administrators retain access. Deleted articles follow the archive restrictions above.

A user with both author and editor roles may approve their own article. Before first publication, editors return another author's text for correction rather than rewriting it, even if they also hold the author role.

### Published Article Changes

- Each article has at most one unfinished working version, separate from its current published revision.
- Authors propose new versions through the editorial workflow. The current version remains public until the replacement is approved and published.
- Editors can directly change title, summary, body, images, sources, category, and tags without separate approval. Original authorship is preserved.
- Direct editor corrections close any existing author working version as superseded, whether it is a draft, in review, or approved but unpublished. Retain its text and record the reason; notify the author. A version in review is rejected for this reason. The author may create a new working version from the current published text; the closed version cannot later be published.
- The original author remains credited. Revision history records who made changes; readers see the latest published update time.
- Editors can immediately withdraw a seriously incorrect article with a recorded reason. Only the article's author may prepare its corrections while withdrawn; editors review rather than directly rewrite it. Corrections require approval before republication. Holding Editor or Administrator alone does not grant the author's correction operation. Any existing working version becomes an author-editable draft with review and approval reset. Route submitted corrections to the previous reviewer if effective editorial permissions remain, otherwise to the shared queue.
- Article versions and significant editorial actions are retained in history.

### Citizen Story Suggestions

Registered residents can submit informal, unedited topics and materials, including a title, description, event date and location, media, and contact details when needed.

- Suggestions enter a shared queue accessible to all authors, not the editors' review queue.
- An author can take a suggestion into work, assigning it to themselves, or return it to the queue.
- The author can create a linked article, which follows the editorial workflow.
- Once taken into work, the resident cannot change the materials.
- Authors cannot reject or delete suggestions. Only an administrator can delete one manually.
- Suggestion records are fully deleted after 72 hours from creation, without retaining an archived copy. Articles already created from them and materials transferred into those articles survive; article history retains that the topic came from a resident.

Residents can track suggestions in their accounts while those suggestions exist.

### Classified Advertisements

Registered users can save drafts, create, edit, delete, withdraw, and renew advertisements, upload multiple images, and track listing status. Fields include title, description, category, price where applicable, district, and seller contacts.

Planned categories cover electronics and belongings, real estate, vehicles, services, jobs, animals and lost pets, and miscellaneous listings. Categories and their attributes are configurable. Visitors search and filter by category, price, location, and relevant attributes.

Seller contacts are limited to phone and/or Email. There is no advertisement messaging, live chat, or Telegram contact option.

Advertisements require moderator approval before publication. Owners make corrections; moderators do not rewrite listings. Changes to a published advertisement await moderation while its approved version remains public.

Publication lasts seven days. Owners may renew active or expired listings for seven days from renewal. Unchanged content needs no new review. Expired listings leave the public catalog but remain in the owner's account.

Moderators can block advertisements with a mandatory reason, including after publication. Blocked advertisements cannot be relisted or renewed. Owners may correct and resubmit them; they remain hidden until approved.

### Advertisement Reports

Any visitor, including an anonymous visitor, can report an advertisement. Moderators see reported listings, the total report count for each, and each report's detailed text.

Reports do not automatically hide listings; moderators decide whether to block them. Moderators can dismiss reports without an explanation. Reviewed reports and decisions remain in history, and the listing leaves the queue. A new report returns it to the queue with previous decisions visible.

### Article Comments

- Anonymous and authenticated comments appear immediately in one flat list under the article, without replies or nesting.
- Comments cannot be edited. Authenticated users can delete their own account comments; anonymous visitors cannot delete theirs.
- Visitors can report comments. Moderators and administrators can hide any comment; author and editor roles alone do not grant this right.

### Personal Account and Notifications

The account contains Profile, My Advertisements, My News Submissions, Account Settings, and Notifications. Staff also access role-specific work areas.

The profile shows Email, display name, Email confirmation status, and all assigned roles. Confirmed users can change their display name and password. Email changes, account deletion, and additional profile settings are outside stage 1.

Role names in the profile use these Russian display labels:

| Role | Profile label |
| --- | --- |
| Regular User | Пользователь |
| Author | Автор |
| Editor | Редактор |
| Moderator | Модератор |
| Administrator | Администратор |

Review outcomes and feedback are delivered to the account and by Email. Profile settings provide separate switches for every notification type and channel. Disabling notifications does not hide decisions or feedback in content records.

Stage 2 notifies the article author of return with feedback, final rejection, approval, publication, direct editorial correction, withdrawal, and closure of a superseded working version. Template submitters receive approval, rejection, archival, block, and unblock notifications. Each type defaults to enabled for both account and Email delivery.

Notify only affected editors about resubmission to their review, author withdrawal/deletion, and review transfer. Do not broadcast new shared-queue articles to all editors. Template block/unblock notifications also go to authors of affected articles, including drafts.

Users may mark account notifications as read and remove them from their own list. Automatically delete these notifications 30 days after creation regardless of read state. Removing a notification does not delete the underlying decision or content history.

Workflow Email may be delivered or manually replayed only within seven days after the event. Automatic retry intervals follow Stage 1. Expiry ends delivery eligibility without removing the underlying decision or history.

Email confirmation and password reset messages are excluded from these switches.

## Editorial and Administrative Workspace

Staff work areas support the workflows above, alongside:

- A dashboard for pending work.
- A media library for uploaded images and media.
- Account, role, portal setting, and integration management.
- Content analytics and operational metrics, with details to be agreed.
- An audit viewer for significant editorial, moderation, and administrative actions.

Stage 2 uploads support JPEG, PNG, and static WebP; SVG and animation are excluded. Each source file is limited to 10 MiB (10,485,760 bytes) and 40 megapixels (40,000,000 pixels). Each article version may reference at most 20 distinct images across its cover, gallery, and body; repeated use of the same image counts once. Other file types require separate agreement. Draft materials are private; public access follows the owning content's publication rules.

Correct image orientation, remove EXIF and other metadata, and create smaller variants for page delivery. Retain only the processed image and its variants, not the source file.

Authors select their own uploads or approved shared image templates. Any author may submit an image to the template library; moderators or administrators review it before it becomes selectable by all authors. Images from other published articles do not automatically become shared templates. Rejection requires a reason; the author can correct the proposal and resubmit. The file of an approved template cannot be replaced; a new image requires a separate proposal and review.

Moderators and administrators can archive a template, preventing new selections while preserving existing article/version references. Its author may request removal using a button in the template card; the request enters the moderator/administrator queue. Staff must archive it rather than reject the request, record the resolution, and notify the author. The request does not directly archive the template. Moderators and administrators can restore an archived template with an unchanged file, recording the action in history; blocking remains independent.

Moderators and administrators can separately block an unacceptable template with a required reason: deny its public image delivery and show a placeholder in articles, including already published articles. They may undo an erroneous block with a reason and history record; images reappear in articles when current publication visibility permits access.

Automatically delete an upload seven days after creation if it has never been attached to an article or template proposal. Images referenced by drafts, retained versions, or history are excluded from this cleanup.

When an article is withdrawn, close public access to its images unless another currently published article uses them. Retained history and private references preserve storage, not anonymous access. Template approval grants library/preview access to authors, not anonymous access: even an approved template is publicly readable only while a currently published article uses it and it is not blocked. A template block overrides publication references.

## Integrations

Email supports account confirmation, password recovery, and workflow notifications from their respective implementation stages. Temporary delivery outages must not prevent otherwise valid business actions, including news publication. Pending messages resume delivery after recovery. Email delivery may occasionally be repeated after a failure; strictly one-time Email delivery is not promised.

Telegram, weather, and RSS are planned as later integrations.

## Excluded and Deferred Scope

A public API is excluded.

The following are outside the initial implementation or subject to later agreement:

- Phone login and third-party login.
- Automatic news import and multi-tenancy.
- General-purpose private messaging.
- Payments, paid advertisement promotion, shipping, and marketplace transactions.
- Bookmarks, popularity statistics, personalized recommendations, and news digests.
- City events calendar and public issue reporting.
- Complex real-time collaboration.
- Scheduled publication, advanced search, and additional media capabilities.

Open details include state models, notification events and defaults, comment-report handling, and remaining module interactions.
