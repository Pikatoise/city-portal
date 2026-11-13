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
| Moderator | Moderate advertisements and comments, handle reports, and restrict ordinary users. |
| Administrator | Manage accounts, roles, portal settings, integrations, and administrative operations. |

Accounts are unified and may hold multiple roles. Access depends on role, ownership, and content state.

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

The preliminary homepage includes editor-selected featured articles, latest news, a smaller recent-advertisement block, and access to categories, search, story suggestions, and posting advertisements. Editors and administrators manage featured articles and homepage content. The page composition and interactions will be agreed during its implementation.

### News and Article Content

The news section provides a chronological feed, categories and tags, search and filters, featured stories, and article pages. Article pages show author information, publication and update times, available media and sources, related articles, and search-friendly URLs and metadata.

Submission requires a title, short summary, body, and one category. The author comes from the account. Cover images, galleries, and tags are optional; sources are required when using material from others. The system checks required fields, and the editor assesses the content.

The visual editor supports paragraphs, subheadings, emphasis, lists, links, quotations, and images with captions. It does not support arbitrary HTML or complex page layout.

Editors and administrators create, rename, and archive categories and tags. Authors select existing entries. Archived entries cannot be selected for new associations; existing associations remain.

### Editorial Workflow

`Draft -> In Review -> Approved -> Published`

1. The author prepares a publication-ready article and submits it. Author changes are blocked during review.
2. An editor approves it, returns it with required feedback, or finally rejects it with a reason.
3. On return, the author corrects the text, briefly describes the changes, and resubmits. This repeats until approval or final rejection.
4. Any editor or the article's author can publish the approved version. The author does not need an editor role for this action.

An author may withdraw a submission for corrections, ending its active review. Final rejection closes the article's consideration: the author retains the text but cannot resubmit that article. Changing approved content invalidates approval and requires another review.

Review is assigned to one editor. Another editor may explicitly take over, with the transfer recorded in history. Approval and publication are recorded as separate actions with their responsible users.

A user with both author and editor roles may approve their own article. Before first publication, editors return another author's text for correction rather than rewriting it, even if they also hold the author role.

### Published Article Changes

- Authors propose new versions through the editorial workflow. The current version remains public until the replacement is approved and published.
- Editors can apply corrections directly without separate approval.
- If an editor changes the published text while an author's version awaits review, that version is automatically rejected and the author receives the reason. The author may create a new version from the current published text.
- The original author remains credited. Revision history records who made changes; readers see the latest published update time.
- Editors can immediately withdraw a seriously incorrect article with a recorded reason. Corrections pass through review before republication.
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

Email confirmation and password reset messages are excluded from these switches.

## Editorial and Administrative Workspace

Staff work areas support the workflows above, alongside:

- A dashboard for pending work.
- A media library for uploaded images and media.
- Account, role, portal setting, and integration management.
- Content analytics and operational metrics, with details to be agreed.
- An audit viewer for significant editorial, moderation, and administrative actions.

The initial upload implementation supports images. Other file types require separate agreement. Draft materials are private; public access follows the owning content's publication rules.

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
