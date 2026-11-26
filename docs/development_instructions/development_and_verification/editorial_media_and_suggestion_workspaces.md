# Editorial, Media and Suggestion Workspaces

| Route | Workspace |
| --- | --- |
| `/account/articles` | Author drafts, submissions and publication. |
| `/account/media` | Author uploads and shared templates. |
| `/staff/reviews` | Editorial queue, assigned reviews and history. |
| `/staff/taxonomy` | Category and tag management. |
| `/staff/publications`, `/staff/articles/{id}` | Homepage curation and published article corrections. |
| `/staff/media` | Template moderation and removal requests. |
| `/account/suggestions` | Resident submission, attachments and status. |
| `/staff/suggestions` | Available proposals with photos and the author's own assignments. |
| `/staff/suggestions/moderation` | Suggestion moderation and reasoned deletion. |

Change staff roles through the administration API or workspace so review and
suggestion releases commit with the role change.

## Suggestion review scenario

1. From the homepage, choose **Suggest a story** and submit a suggestion
   with a photo.
2. Sign in as an Author and open **Resident suggestions**. Preview the photo
   in the available queue and check that the resident's contacts are hidden.
3. Claim the suggestion. In **My assignments**, select a ready attachment
   and open the draft.
4. Submit the completed article. Approve it as an Editor, then publish it
   as the Author.
5. In the resident's account, check the link to the published article and
   the notification. On the public article, check the resident's idea credit.

To exercise a failure path, try claiming the same queued suggestion from
two Author accounts and confirm that only one claim succeeds. To exercise a
return, return an unpublished suggestion and check that the draft is gone
and the remaining queue time resumes.
