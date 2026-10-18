# ADR 0003 — Scraper service: draft pull requests from retailer listings

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-591). Founded on Armamentarium ADR 0118 §4 and its Addendum ARMAM-591.

**Overridden in part by ADR 0004** (ARMAM-593): §2 "once a day" (now the 1st and 15th), the token's "at most 90 days"
(now without expiry); while `--auto-merge` runs also §10 "draft … no ids; the maintainer adds ids … before merging" and
§12 "until five scraper pull requests are open"; and Rejected "Ids by exact name match against the catalogues".

**Narrowed by Addendum ARMAM-601:** §10 box name only from a listing that is a source of the box.

**Changed by Addendum ARMAM-596:** `www.fantasywelt.de` leaves §5; a 403 counts as "does not answer" (§12), no longer
as no finding (Context, last point).

**Changed by Addendum ARMAM-595:** §6 "at most one request per five seconds and host" is stretched up to ten seconds for a
host that throttles, and a 429's `Retry-After` is honoured; §12 "the product comes back in three days" holds, and it is
asked once more at the end of the same run first.

## Context

The table grows only by hand so far. Measured on 2026-10-17:

- No source names catalogue ids. The product catalogue `WarHub/warhub-catalog` links `productCode` and `ean`, names game
  system, category and status, but carries no contents (`quantity` is always 1) and no release date per product.
- Retailer shops list a box's models as lines `N x Name` in their product description. Shopify shops find a product by EAN
  (`/search/suggest.json`) and confirm it in `/products/<handle>.js` (`variants[].barcode`); their bulk lists carry no
  barcode. JTL shops find it by EAN (`/?qs=`) and confirm it in microdata (`itemprop="gtin13"`). Element Games finds it
  by article number only and names it on the page.
- German shops translate model names (`Phantomwächter` for `Wraithblades`); the order of the lines follows the publisher's
  text.
- `warhammer.com` (AWS WAF captcha), `pk-pro.de` (Cloudflare challenge) and `games-island.eu` (captcha; its catalogue API
  carries no EAN, no article number and no model count, and the shop declares a text and data mining reservation) block or
  give nothing. `warhammer-community.com` has no product search and states counts only in prose.
- Added on the maintainer's list (2026-10-17): `miniset.net` keeps one page per article number (`/sets/gw-<code>`, the
  canonical address confirms it) with the box's list, and restricts a client after about a dozen requests at five
  seconds ("temporarily restricted due to suspicious automated activity"). `www.retrofixgames.com` is a Shopify shop
  whose terms forbid scraping ("spider, crawl, or scrape"). `wh40k.lexicanum.com` and `whfb.lexicanum.com` sit behind a
  Cloudflare challenge; `www.fauxhammer.com` is a blog without box lists.
- Single kits are described in prose by every shop measured ("six Wolf Scouts"); lists `N x Name` appear for boxes of
  several kits. `www.fantasywelt.de` answers the .NET client with 403 behind Cloudflare while curl gets 200; it stays
  listed, a 403 is no finding.

## Decision

1. **Project** `src/Armamentarium.BoxData.Scraper` (console, .NET 10), tests in `tests/`, solution
   `armamentarium-box-data.slnx`. No package reference in the service; MSTest only in the test project.
2. **Runs** once a day on the project's server: systemd timer, `oneshot` service, `DynamicUser`, state in its
   `StateDirectory`, the token through `LoadCredential`. Unit, timer and deploy script live in `deploy/`.
3. **GitHub** through its REST API with `HttpClient` (git data API, pulls). No clone, no git on the server.
4. **Candidates:** every Games Workshop product of category `miniatures` in the systems of ADR 0002 that no box holds yet,
   read once per run from the compiled address `https://warhub.github.io/warhub-catalog/products.json`. That request is not a
   shop: it sits outside the allowlist and the throttle, and no hash or manifest is checked, because a wrong catalogue entry
   only costs a search; nothing from it enters the table but article numbers, which every shop must confirm.
   Order: Kill Team first, then `current` before discontinued, then article number.
5. **Sources** are a compiled allowlist with four adapters:
   - Shopify: `www.wellplayed.ch`, `www.irresistibleforce.com.au`, `tistaminis.com`, `abyssgamestore.ca`, `radaddel.de`,
     `www.retrofixgames.com` (its terms forbid scraping; the maintainer carries that risk, ADR 0118 Addendum ARMAM-591).
   - JTL shop: `www.fantasywelt.de`, `taschengelddieb.de`.
   - Element Games: `elementgames.co.uk`.
   - miniset.net: `miniset.net`, by article number.

   A listing counts only when the shop confirms the EAN (Element Games and miniset.net: the article number). Used goods are skipped. A
   captcha or challenge answer is no finding; it is never circumvented.
6. **Throttle:** at most one request per five seconds and host, per minute for `miniset.net`; hosts run in parallel. A
   page that says the host restricts this client, whatever its status, rests the host for the rest of the run. A slow
   source (miniset.net) is asked only when a fast one found the product and the fast findings alone would not make a
   proposal (one listing, or an entry with a single source), since alone it never makes two sources; so the five-second
   hosts set the pace and the first pass over the backlog takes about five hours.
7. **User agent:** the service's own name, `ABD/1.0`, without a contact address (maintainer decision; it
   overrides "with contact address" of ADR 0118 §4, see its Addendum ARMAM-591). A desktop browser string was tried and
   measured worse: three Shopify shops answered `/products/<handle>.js` with 429, which they answer with 200 to the own name
   (2026-10-17).
8. **Only facts:** a number and a model name per line. Terrain, datacards, dice, markers, tokens, mats, rule books,
   dossiers, rulers, transfers, bases, part counts, paints, brushes, clippers, tools, construction guides and handbooks are
   dropped, and so is every line under a heading that names one of them: a counted heading ("15 plastic terrain pieces"),
   a line ending in a colon ("these interchangeable parts:"), or a counted line without a bullet that ends in one ("5x
   Citadel Colour paints, including:"). A counted line without a bullet whose bulleted lines below add up to its count
   ("11x Space Marines" over "1x Captain", "5x Terminators", "5x Infernus Marines") is their total and dropped; a model
   whose equipment is listed below it with the same count would be lost with it, which the maintainer's check catches. The
   name ends at a spaced dash or at a colon followed by four words or more, where the shop's description begins; a
   shorter colon belongs to a title ("Warhammer 40,000: Battle Report Book"). An excluded word or an "and 2" in that
   description drops or splits nothing, nor does an "and 2" inside a parenthesis; an excluded word in a parenthesis or
   after a comma still drops the line. "10 Termagants and 1 Ripper Swarm" (or "and 1x") is two lines, a non-player
   marker counting for both (measured on the first server run, Armamentarium ARMAM-593). An
   alternative build named on a line is dropped and the line counted as its base build, noted in the pull request.
9. **Agreement:** names are compared normalised (lower case, without `operative`, `miniature`, `non-player`, plural `s`,
   punctuation). The same count in two sources gives `count`; different counts give `countCandidates`. A German source
   confirms lines only by position, when its number of lines and its sequence of counts equal an English source's. Every
   entry needs two confirming sources; one entry short and the whole box waits. So does a box whose listing names one
   model twice ("2x Sergeant (bolt)", "1x Sergeant (plasma)"): no count would be one a source states.
10. **Output:** one **draft** pull request per box, branch `scraper/<lowest article number of the box>` (language variants:
    the lowest added one). The entries carry name and count, no ids; the maintainer adds ids, readings and catalogue names
    and checks the preset roles in the branch before merging. Where the line says non-player, `role` is `npo` in a `kt` reading and `extra` in any other
    (README, Entry); else `team`. The box name comes from an English shop title, else from the catalogue name with a note;
    a German title never. A Combat Patrol gets `generation` from the catalogue status (`current`, else `legacy`), marked for
    checking. The body names every source of the box and every note. Shop text enters the table, the commit and the pull
    request only as one cleaned line (no control or format characters, at most 120 characters), and the body shows it only
    in code spans, so it cannot render links, images or mentions. A failed proposal (also a timeout or a stop) is logged, its
    branch deleted again, the product comes back in three days, and the run goes on.
11. **Changed box** means a new language variant: a variant of the same shop product whose barcode belongs to a box already
    in the table adds its article number. Counts of existing boxes are never touched.
12. **Pace:** the run works through the backlog until five scraper pull requests are open; a box with an open pull request
    is skipped, so hand edits in its branch are never overwritten. A product without two confirming sources is checked
    again after 30 days. A shop that does not answer (network, timeout, 429, server error) is not a "not listed": while
    one failed and fewer than two confirm, the product comes back in three days. After 20 such failures in a row a shop
    rests for the rest of the run. A shop that answers with something unreadable (a changed page) has answered: logged,
    no finding, the product rests as usual, so a broken parser cannot bring the whole backlog back every day.
13. **Failures** go to the journal only.

## Rejected

- **Ids by exact name match against the catalogues:** shop names rarely equal catalogue names; catalogues on the server for
  few hits.
- **Report only, no table change:** every row by hand again.
- **One collecting pull request per run, or one running pull request:** blocks partial merges and collides with hand edits.
- **Proposing an entry from a single source:** each number from two sources is the mitigation ADR 0118 §2 relies on.
- **Octokit, git CLI:** a dependency, or git plus a token in a remote URL on the server.
- **Bulk shop lists:** carry no barcode.
- **Prose sources, slug matching on warhammer-community.com:** assigning an article to a box by name is guessing.
- **Circumventing captchas and challenges:** never. Lexicanum stays out. Its wikiteam dumps on archive.org (2026-06-04)
  were measured as the legitimate way and do not help: box pages carry neither article number nor EAN, hardly any box has
  a page, and the text is CC BY-NC.
- **`www.fauxhammer.com`:** reviews and news, no box lists, no article numbers.
- **A browser user agent:** drew 429 from Shopify shops (§7).
- **Mail or an issue on failure:** a new channel or a wider token for a daily job whose missing pull requests show anyway.

## Addendum ARMAM-601 — the box name comes from a source (2026-10-18)

- §10 "the box name comes from an English shop title" is narrowed: **only the title of a listing that is a source of the
  box** (a confirming listing of the agreement; for a Combat Patrol, a confirming shop). A shop that matched the barcode
  but listed no contents confirms nothing, and its title named box 60010213001 "Horus Heresy: False Gods (PB)" while its
  sources, the catalogue and the contents are the Ogor Mawtribes Army Set. Without such a title the catalogue name stands,
  with a note, as before. The box is renamed "Age of Sigmar: Ogor Mawtribes Army Set".
- Readings of a Kill Team product: ADR 0002 Addendum ARMAM-601.
- Rejected: **always the catalogue name** (warhub lists a language variant first, often in French and in capitals; the
  README asks for the English product name).

## Addendum ARMAM-596 — a refusing shop has not answered (2026-10-18)

- **Measured:** `www.fantasywelt.de` answered every request of the first auto-merge run with 403 (2785 of 2785; before
  that 183 of 183 and 366 of 366). Cloudflare refuses the .NET client; curl gets 200. As "no finding" the 403 reset the
  host's failure count, so the shop was asked for every product of the run and gave nothing.
- **`www.fantasywelt.de` leaves the allowlist** (§5). The JTL adapter stays for `taschengelddieb.de`.
- **A 403 is a shop that does not answer** (§12), like 429 and a server error: the shop's guard refuses this client, which
  says nothing about the product. It counts towards the 20 failures in a row after which the host rests for the run, and
  a product with fewer than two confirming sources comes back in three days instead of 30. §5 "a captcha or challenge
  answer is no finding" holds for what it says, a challenge confirms nothing; it is never circumvented. Other answers that
  are not 200 (404) stay no finding.
- Rejected: **only removing the shop** (the next shop that refuses this client costs the whole run again); **only resting
  it** (fantasywelt would cost 20 requests per run without a prospect); **a separate 403 counter** that keeps "no
  finding" (more code for the same rest, and the product would wait 30 days for an answer it never got); **working around
  the guard** (browser fingerprint, challenge answers): never.

## Addendum ARMAM-595 — a throttling shop is asked again in the same run (2026-10-18)

- **Measured** on the first auto-merge run (2026-10-17 20:20 to 2026-10-18 03:48, 3372 products): 3598 answers 429, all
  of them to `/products/<handle>.js` of six Shopify shops (abyssgamestore.ca 1130, radaddel.de 910,
  www.irresistibleforce.com.au 771, www.retrofixgames.com 702, www.wellplayed.ch 71, tistaminis.com 14), never to
  `search/suggest.json`; about 200 to 280 an hour per shop, steadily, no burst. Never 20 in a row, so no shop rested: a 200
  or 404 in between reset the count. Whether the shops send `Retry-After` was not measured, the journal logged no headers.
  Up to 886 products came back in three days because a shop that throttled may have held the second source.
- **`Retry-After` of a 429 is honoured** (seconds or HTTP date): until then a request to the host fails at once, without
  a request and without counting towards the 20 in a row. A wait longer than ten minutes rests the host for the run. No,
  zero, negative or unreadable value is no header.
- **Stretched interval** (§6): from a host's third 429 in a run on, its interval is doubled up to ten seconds and stays so
  for the run; a longer interval (miniset.net) is never shortened, other hosts are untouched. Every product waits for its
  slowest fast host, so ten seconds is the cap: a Shopify product (two requests) takes 20 s and a pass over the whole
  backlog about 19 hours, inside the unit's 20.
- **One catch-up pass** (§12): a product that a shop did not answer and that fewer than two sources confirm keeps its
  three-day mark and is asked once more, whole, after the pass over the backlog; not when the run stopped at five open
  pull requests. A failure that the same run cannot get past does not bring it back: a 403 (the guard, Addendum ARMAM-596),
  a host resting for the run, a block page. The run ends with `catch-up: N asked again, M still without an answer`.
- **Measure** at the end of every run, also a stopped one: one line per host that answered 429, with the number of 429,
  how many carried `Retry-After`, and the interval reached. The baseline is the first run above.
- `Retry-After` only for 429; a 5xx stays as it was (§12).
- Rejected: **a fixed longer interval for Shopify** (slows every run, throttled or not, and asks nothing again);
  **stretching without `Retry-After`** (wastes the wait a shop names); **waiting until `Retry-After`** (every product waits
  for every shop, the whole run would stand); **halving the interval again after successes** (more state, no gain measured
  on a steady 429 pattern); **asking only the failed shops again** (keeps listings across the run for little; the good
  shops answer again at their own pace); **several catch-up passes** (run length beyond estimate); **a 403 throttles like a
  429** (more requests to a guard that refuses this client); **`Retry-After` for 503 too** (no 5xx pattern measured);
  **an own time budget for the catch-up** (the state is saved per product, the unit's timeout already bounds the run).
