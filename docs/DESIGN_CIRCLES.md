# Design — Circles (invite-scoped pools, additive)

_2026-09-17. Source: Product Opportunity Critic #3, opportunity 1 (strongest ungated bet). Status: approved by the loop under the current vision; the positioning question is Decision 5 in `OWNER_DECISIONS.md`._

## Why

Every remaining product bet is "gated on pool density", and Decision 4 assumes "a small private cohort" — yet the product has no group primitive: one global pool (`MatchesController.Index` loads every fingerprint), registration open, the only growth lever a bare `/account/register` link. Circles are the one lever on pool *formation*: an organiser (meetup, Discord, course cohort, company) shares a link; members find each other first, and the pool grows by communities rather than by strangers.

## What a circle is

A **circle** is a named group with a shareable invite link. Joining tags your account with membership. On the match list, people in a circle you are also in carry a **"Same circle: <name>"** chip and there is a **"Same circle first"** sort. Nothing else changes: registration stays open, the pool stays global, similarity ranking stays the default, and a circle is never a filter, never part of any score.

**Circle view (added after Opportunity Critic #4).** The chip and sort decorate the global list, which is cut at a 5% similarity floor and the top 20 — so a circle-mate below the floor or outside the top 20 was invisible, and the promise "your own group finds each other first" failed exactly for mixed groups (a team, a course cohort). `/circles/{id}` is a separate page for members only: every discoverable, not-suspended member (minus anyone hidden between the viewer and them), with a tier where there is overlap and "No overlap yet" below the floor, no top-20 cut, and bio/contact by the same reciprocity rule as the match list. Members see members — that is what joining a group means, and it is stated on the join page; nobody outside the circle sees the list. The global list is untouched: this is a second view, not a filter of the first.

Deliberately **not** in v1: owners/admins, private circles, inviter links (who invited whom is never recorded), circle discovery/search, member lists visible to non-members.

## Data (new-data checklist applied)

```
Circle            { Id, Name (≤40 chars, TextPolicy.ValidateProfileText, rendered plain), CreatedAt }
CircleMembership  { Id, CircleId (FK cascade), UserId (FK cascade), JoinedAt }   unique (CircleId, UserId)
```

- **Opt-in**: membership is created only by an explicit "Join" POST on a page that names the circle. A link click alone never joins; a logged-out visitor registers or signs in first (the invite rides along as a hidden field), then confirms.
- **Skippable**: nothing requires a circle.
- **Minimal derived form**: membership only. No inviter, no join source, no per-circle activity.
- **Deletable alone**: "Leave" on the dashboard removes the membership row. Account deletion removes every membership (FK cascade **and** explicit `RemoveRange`, like blocks). A circle with no members left is deleted with the last membership.
- **In export**: `Circles: [names]`.
- **Withheld while hidden**: the chip and the sort are computed only when the viewer is discoverable (reciprocity rule shared with bio/contact/intent).
- **Never a hard filter, never blended**: chip + sort only; the interest ranking remains the default and the tiebreaker.
- **Explainable**: the chip names the circle.
- **Hideable by config**: `Signals:CirclesEnabled` (default true) hides create/join/chip/sort; rows are kept.
- **DB-asserted tests**: an invite token is never persisted; deleting an account removes its memberships; leaving removes only that membership; an empty circle disappears.

## Invite links

`/circles/join/<token>` where `token = DataProtection(purpose "circle-invite.v1").Protect("<circleId>|<issued>", 30 days)`. Any member can show the current link on their dashboard (a fresh token each time; old ones stay valid until they expire). Nothing about the token is stored. A tampered or expired token renders "This invite link has expired — ask for a new one."

Register and login accept an optional `circle` query parameter (only while circles are enabled) and carry it as a hidden field. Login redirects straight to the join page; registration shows the recovery code first, with "join the circle I was invited to" as its continue button, consumed on that one render. The join page shows the member count to whoever holds the link — the link is the credential, and joining would reveal the count anyway. Starting or joining is metered per IP (`RateLimiting:CirclesPermitLimit`) and capped at 20 circles per account.

## Circle view

`GET /circles/{id}` (members only, else 404; 404 while circles are disabled). Members ordered by similarity to the viewer, then username; members without a fingerprint listed last as "No fingerprint yet". A percentage is shown only at or above the floor (no false precision below it). The viewer's own hides apply; a hidden (non-discoverable) member is withheld from others but still sees the circle. The page carries the invite link and Leave. Linked from the dashboard card, and from the match list's empty state ("see who's in your circle").

## Matches

For the viewer: `myCircles = memberships.Where(UserId == me)`. One query joins the match candidates' memberships against `myCircles`, producing `SharedCircles: List<string>` per match (names, alphabetical). Sort `"circle"` is offered only when the viewer has ≥1 circle: `OrderByDescending(m => m.SharedCircles.Count > 0)` — stable, so interest order is preserved within each group.

## Dashboard

"Your circles" card: each circle with member count, the invite link (readonly input + copy button, same pattern as the empty-state invite box) and Leave; a "Start a circle" form (name). Member counts are shown only to members — they invited each other, so no k-anonymity concern.

## Metrics

`/metrics`: `circles` and `usersInCircles` (plain totals, like `withBio`).

## Slices

1. Model + migration + `CirclesController` (create, join page, join, leave, invite link) + dashboard card + export + delete + register/login carry-through + tests.
2. Match chip + sort + metrics + docs (handbook §2/§3/§5, README journey).

## Validation

`dotnet build -warnaserror`; `dotnet test` ≥ 371 (385 after both slices); integration: two invitees see each other's chip, an outsider sees none, hidden viewer sees none, leave removes chip, account deletion removes memberships, tampered token rejected, register-with-invite lands on the join page; QA walk on `profiler-web-qa` (done 2026-09-17: dashboard card, member invite page, expired page); independent Release Auditor (done 2026-09-17, verdict yes; findings fixed).
