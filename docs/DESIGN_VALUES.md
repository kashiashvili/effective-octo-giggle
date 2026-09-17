# Design — Values & worldview profile v2 ("schwartz-v2")

_2026-09-17. Owner instruction: "the values profile is too hidden; make it accessible; make it science-based — research how to determine a person's values and worldview and implement it." This resolves Decision 2 as **strengthen** (the loop's earlier recommendation to hide by default is withdrawn). Standing constraints unchanged (`CLAUDE.md` §5): no clinical or personality claims, no licensed psychometric items, no political or moral items; the signal stays separate, explainable, opt-in, skippable, deletable, exported, withheld while hidden, never blended into the interest score._

## 1. What the science supports

- **Values, not traits.** Schwartz's theory of basic human values defines ten values as motivational goals (self-direction, stimulation, hedonism, achievement, power, security, conformity, tradition, benevolence, universalism) arranged on a circular continuum and summarised by two bipolar dimensions: *openness to change vs conservation* and *self-transcendence vs self-enhancement*. Hedonism sits between openness and self-enhancement and shares elements of both. Cross-culturally replicated in 49 groups (Schwartz & Cieciuch 2022). This is a self-declared priority ordering, not a personality assessment — exactly the framing the product needs.
- **Similarity in values is what predicts connection.** Friends are more similar in values than non-friends even controlling for demographics (dyadic correlation ≈ .38 across the value system; Benish-Weisman et al., adolescent friendships); among committed couples value similarity predicts relationship satisfaction where trait similarity does not (Finnish couples, PAID 2018); and a meta-analysis finds similarity breeds attraction most strongly at **zero acquaintance** — the situation every Profiler match starts in.
- **Relative priorities, not absolute ratings.** Schwartz's scoring centres every rating on the person's own mean ("scale-use correction"), which removes acquiescence and social-desirability differences and turns ratings into *priorities*. Similarity between people is then a distance between priority profiles. v1 measured one axis with four agree/disagree items and no centring; its bucket clumped 95% of people into three levels (`ValuesSignalResolutionTests` v1) — that weakness, not the idea, was the problem.
- **Short forms work.** The Short Schwartz's Value Survey (Lindeman & Verkasalo 2005) rates each of the ten values once, with a plain description, and reproduces the circular structure with good reliability; brief four-dimension inventories (17 items, JPA 2024) and the ESS 21-item form are also validated. Ten value items plus four worldview items is a two-minute questionnaire.
- **Worldview.** *Primal world beliefs* (Clifton et al. 2019; PI-18/PI-6) are validated, stable beliefs about the world's basic character — **Safe** (vs dangerous), **Enticing** (vs dull), **Alive** (vs mechanistic) under one **Good** belief. The *items* ask nothing political, moral or religious, which is what the product claims; the *constructs* are not ideology-free — dangerous-world belief is a well-established correlate of authoritarianism and social conservatism in the dual-process literature, so no surface may claim these beliefs are unrelated to politics. Alive edges into spirituality and is left out.
- **Licensing.** The Schwartz instruments are published under CC BY-NC-ND (no commercial use, no derivatives); the Primals Inventory's product-use terms are not stated. Every item below is **original wording** over the published constructs. Only the constructs and the scoring method are borrowed.

Sources: Schwartz & Cieciuch, *Assessment* 2022 (PVQ-RR in 49 groups); Schwartz et al., *JPSP* 2012 (refined theory); Lindeman & Verkasalo, *J Pers Assess* 2005 (SSVS); *J Pers Assess* 2024 (17-item four-higher-order inventory); Benish-Weisman et al., value similarity in adolescent friendships; *PAID* 2018 and 2023 (value vs trait similarity and relationship satisfaction); Clifton et al., *Psychological Assessment* 2019 and Clifton & Yaden 2021 (primals, PI-18/PI-6). Links in `PRODUCT_LOG.md` §11 entry.

## 2. Instrument (original wording)

**Part 1 — what matters to you.** "How important is each of these to you, as a guiding principle in your life?" 1 = not important to me … 7 = extremely important. Ten items, one per basic value: independence (self-direction), excitement (stimulation), enjoyment (hedonism), achievement, influence (power), security, fitting in (conformity), tradition, loyalty to close people (benevolence), fairness for everyone and the natural world (universalism).

**Part 2 — how you see the world.** Agreement 1–7 with four short statements: two on Safe (one reversed), two on Enticing (one reversed).

## 3. Derivation (nothing but the result is stored)

1. Centre the ten value ratings on the person's own mean (priorities, not ratings).
2. Four higher-order priorities = mean of their centred values; hedonism counts half to openness and half to self-enhancement, as the theory places it.
3. Quantise each to −2..+2 (round, clamp). Worldview: each dimension = mean of its two items (reversed where needed) centred on the scale midpoint, quantised to −2..+2.
4. Stored: six small integers and the scheme version (`ValuesProfileJson`, `ValuesScheme = "schwartz-v2"`). Answers are discarded in the request — the same promise the fingerprint makes, DB-asserted by test.

## 4. Comparison and explanation

- Values distance = mean absolute difference across the four priorities (0..4). Tiers: ≤ 0.5 **Similar priorities**, ≤ 1.25 **Some overlap in priorities**, else **Different priorities**. Worldview distance the same way over two dimensions.
- The card line is explainable: "Similar priorities — you both put *caring for people and the planet* first" or "Different priorities — you differ most on *stability and tradition*", plus "similar / different view of the world" when both answered part 2.
- "Similar outlook first" sorts by the values tier, then the worldview tier; interest order within each. Never a filter, never blended.
- A person also sees **their own profile** (four priorities and two world beliefs, in words) on the values page and the dashboard — self-insight is part of why anyone answers.

## 5. Accessibility (the "too hidden" half)

Saving interests lands on the match list, whose nudge offers the questionnaire with a link while it is unanswered (no separate hand-off text); dashboard quick action after the source actions when missing, with the profile summary once set; profile page link; values page explains the science in three sentences with sources; landing step 1 mentions it. Every one of these follows `Signals:ValuesEnabled`.

## 6. Privacy checklist

Opt-in with consent; skippable; six coarse integers only; answers never stored (test scans the DB for answer values); deletable alone and with the account; in export; withheld while hidden; hideable by `Signals:ValuesEnabled`; never a filter, never part of the score; explainable line; no clinical or personality wording anywhere. v1 buckets (`openness-v1`) are not comparable and are cleared by the migration — no live users exist; everyone is invited to answer the new form.

## 7. Validation

Unit: derivation rules, centring removes scale use, hedonism split, reversal, JSON round-trip (all six keys required), tiers, explanations, unique-top-priority guard, item wording free of political/moral/clinical terms. Synthetic resolution test (population with realistic priority structure + noise): each dimension uses all five levels, random pairs are **not** mostly "similar", pairs from the same latent profile read similar/overlap far more often than independent pairs. Integration: a scan of every table for item keys and answer values after a submit, consent required (and answers kept selected when it is missed), removal, card line and explanation, sort, withheld while hidden, export, accessible names on every scale point, and each surface gated by `Signals:ValuesEnabled`. Release Auditor run 2026-09-17 (seven P2s, all fixed); QA walk 2026-09-17 before and after those fixes — full round trip, own-profile summary, legend placement, accessible names, answer retention on a validation error.
