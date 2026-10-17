# ADR 0003 — Scraper service: draft pull requests from retailer listings

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-591). Founded on Armamentarium ADR 0118 §4 and its Addendum ARMAM-591.

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
   name ends at a colon or a spaced dash, where the shop's description begins; an excluded word or an "and 2" in that
   description or in a parenthesis drops or splits nothing. "10 Termagants and 1 Ripper Swarm" is two lines, a
   non-player marker counting for both (measured on the first server run, Armamentarium ARMAM-593). An
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
