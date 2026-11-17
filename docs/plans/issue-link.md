# Replace public article URLs

## Status

**Implemented; verification in progress.** The local review stand has been migrated to the new public address format.

The owner requested a complete replacement of the old public URL system. Do not retain old public routes, redirects, alias handling, or other compatibility code merely for the old format. The internal article GUID remains in the database and can continue to be used by internal operations.

## Agreed behavior

- Use `/news/{transliterated-title}-{dd-MM-yyyy}-{guid-suffix}`. For example, an article titled `Тест`, first published on 17 November 2026, could have `/news/test-17-11-2026-a1b2c3`.
- Derive the suffix from the final **six hexadecimal characters** of the article's existing GUID. It remains unchanged throughout the article's lifetime.
- Derive the date from the first-publication instant in the city's configured time zone. Preserve that date so subsequent changes to the configured time zone do not change the URL. Later corrections or republication do not change the first-publication date.
- Transliterate the current published title into a readable URL segment. Pending, unapproved edits do not affect the public URL.
- When a changed title becomes public, replace the address. For example, `Тест 2` becomes `/news/test-2-17-11-2026-a1b2c3`. The previous address stops working; do not redirect it.
- If another article already owns the complete proposed address, reject publication and tell the user to change the title. Do not extend the suffix. Enforce this rule under concurrent publications and direct editorial corrections as well.
- Convert the existing published article to the new address. Do not modify its title, content, author, or publication history to accomplish this.

## Implementation work

Address generation uses lowercase Russian transliteration (including `ё` as `yo`, `й` as `y`, `х` as `kh`, and `щ` as `shch`). Hard and soft signs are omitted. ASCII letters and digits are retained; punctuation, whitespace, and unsupported characters separate words. Consecutive separators collapse, and an empty result becomes `article`. The title segment is limited to 120 ASCII characters without splitting a transliteration unit or leaving a trailing separator.

1. Define one deterministic, server-side transliteration and URL generation routine. Specify its handling of punctuation, unsupported characters, empty results, and length limits before relying on it for persisted addresses. The conversation did not prescribe a complete transliteration table.
2. Persist the first-publication local date and the current public address, or use an equivalent design that preserves the agreed behavior and makes complete addresses unique under concurrent writes. Backfill existing published articles using the available installation time zone and publication timestamp; do not reinterpret an already stored date after later time-zone changes. Verify the applicable historical time-zone data for the existing article.
3. Apply address generation and collision handling to first publication, republication, and direct editorial corrections. Keep URL updates atomic with publication and revision changes. Return an actionable validation error when a complete address is already owned by another article.
4. Replace public article lookup and frontend routing with the new address. Update all discovery cards, featured links, related articles, canonical metadata, generated API contracts and client code as applicable. Preserve the existing visibility rules for current published and withdrawn articles, without exposing drafts or private data.
5. Remove the old public route and its alias lookup, redirects, tests and obsolete contract descriptions. Remove outdated statements about retaining historical public URLs from the current specifications, including `docs/stages/STAGE_2.md`. Keep genuinely historical verification evidence identifiable as historical rather than presenting it as current behavior.
6. Update the affected specifications and acceptance criteria to describe the implemented contract. Apply the migration and update the local review stand without discarding its data. Confirm the one existing published article has a new address and can be opened from the news list.

## Verification

- Verify Russian transliteration, date formatting, six-character GUID suffix, punctuation and edge cases.
- Verify the initial publication, title correction, pending edit, republication, and a later time-zone change. Confirm the date and suffix remain stable, while the title segment follows the current published title.
- Verify that the old title address and the old ID-based public route no longer resolve, including after a title change. Confirm no redirect or obsolete alias behavior remains.
- Verify a complete-address collision produces an actionable error and does not publish or partially change either article, including concurrent attempts and direct corrections.
- Verify anonymous access, withdrawal behavior, private-content isolation, discovery links, related links and canonical metadata.
- Run relevant backend and frontend tests and builds, then verify the existing article through the local HTTPS news list and its new public URL. Record the actual results and any limitations in the stage documentation.

## Current context

The earlier issue where the news list double-encoded a Russian URL was separately fixed in the frontend and checked against the local stand. That fix does not implement the new URL format. The owner has explicitly rejected retaining the old URL system for compatibility.
