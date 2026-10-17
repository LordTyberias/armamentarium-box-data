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
- Single kits are described in prose by every shop measured ("six Wolf Scouts"); lists `N x Name` appear for boxes of
  several kits. `www.fantasywelt.de` answers the .NET client with 403 behind Cloudflare while curl gets 200; it stays
  listed, a 403 is no finding.

## Decision

1. **Project** `src/Armamentarium.BoxData.Scraper` (console, .NET 10), tests in `tests/`, solution
   `armamentarium-box-data.slnx`. No package reference in the service; MSTest only in the test project.
2. **Runs** once a day on the project's server: systemd timer, `oneshot` service, `DynamicUser`, state in its
   `StateDirectory`, the token through `LoadCredential`. Unit, timer and deploy script live in `deploy/`.
3. **GitHub** through its REST API with `HttpClient` (git data API, pulls). No clone, no git on the server.
4. **Candidates:** every Games Workshop product of category `miniatures` in the systems of ADR 0002 that no box holds yet.
   Order: Kill Team first, then `current` before discontinued, then article number.
5. **Sources** are a compiled allowlist with three adapters:
   - Shopify: `www.wellplayed.ch`, `www.irresistibleforce.com.au`, `tistaminis.com`, `abyssgamestore.ca`, `radaddel.de`.
   - JTL shop: `www.fantasywelt.de`, `taschengelddieb.de`.
   - Element Games: `elementgames.co.uk`.

   A listing counts only when the shop confirms the EAN (Element Games: the article number). Used goods are skipped. A
   captcha or challenge answer is no finding; it is never circumvented.
6. **Throttle:** at most one request per five seconds and host; hosts run in parallel.
7. **User agent:** the service's own name, `ABD/1.0`, without a contact address (maintainer decision; it
   overrides "with contact address" of ADR 0118 §4, see its Addendum ARMAM-591). A desktop browser string was tried and
   measured worse: three Shopify shops answered `/products/<handle>.js` with 429, which they answer with 200 to the own name
   (2026-10-17).
8. **Only facts:** a number and a model name per line. Terrain, datacards, dice, markers, tokens, mats, rule books,
   dossiers, rulers, transfers, bases and part counts are dropped, and so is every line under a heading that names one of
   them ("15 plastic terrain pieces"). An alternative build named on a line is dropped and the
   line counted as its base build, noted in the pull request.
9. **Agreement:** names are compared normalised (lower case, without `operative`, `miniature`, `non-player`, plural `s`,
   punctuation). The same count in two sources gives `count`; different counts give `countCandidates`. A German source
   confirms lines only by position, when its number of lines and its sequence of counts equal an English source's. Every
   entry needs two confirming sources; one entry short and the whole box waits.
10. **Output:** one **draft** pull request per box, branch `scraper/<productCode>`. The entries carry name and count, no ids;
    the maintainer adds ids, readings and catalogue names in the branch before merging. `role` is `npo` where the line says
    non-player, else `team`. A Combat Patrol gets `generation` from the catalogue status (`current`, else `legacy`), marked
    for checking. The body names every source of the box and every note.
11. **Changed box** means a new language variant: a variant of the same shop product whose barcode belongs to a box already
    in the table adds its article number. Counts of existing boxes are never touched.
12. **Pace:** the run works through the backlog until five scraper pull requests are open; a box with an open pull request
    is skipped, so hand edits in its branch are never overwritten. A product without two confirming sources is checked
    again after 30 days.
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
- **Circumventing captchas and challenges:** never.
- **A browser user agent:** drew 429 from Shopify shops (§7).
- **Mail or an issue on failure:** a new channel or a wider token for a daily job whose missing pull requests show anyway.
