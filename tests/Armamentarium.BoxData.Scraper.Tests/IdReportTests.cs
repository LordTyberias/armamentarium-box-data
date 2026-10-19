using System.Text;

namespace Armamentarium.BoxData.Scraper.Tests;

// The catalogues and the table below are written for these tests; none is copied from BSData or the committed table.
[TestClass]
public sealed class IdReportTests
{
    private const string Iron = """
        <catalogue id="cat-iron" name="Iron Brothers" library="false">
          <selectionEntries>
            <selectionEntry type="unit" id="se-captain" name="Captain"/>
            <selectionEntry type="unit" id="se-troopers" name="Troopers"/>
          </selectionEntries>
        </catalogue>
        """;

    private const string Rust = """
        <catalogue id="cat-rust" name="Rust Brothers" library="false">
          <selectionEntries>
            <selectionEntry type="unit" id="se-rust-troopers" name="Troopers"/>
          </selectionEntries>
        </catalogue>
        """;

    private const string Table = """
        {"format": 1, "boxes": [{
          "name": "Box", "productCodes": ["60010199001"], "sources": ["https://shop.test/a", "https://shop.test/b"],
          "readings": [{"reading": "40k", "entries": [
            {"role": "team", "name": "Captains", "count": 1},
            {"role": "team", "name": "Troopers", "count": 5},
            {"role": "team", "name": "Wizard", "count": 1},
            {"role": "team", "name": "gaming boards", "count": 2}]}]}]}
        """;

    [TestMethod]
    public void Write_NamesEveryGapWithItsReason()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron.cat", Encoding.UTF8.GetBytes(Iron));
        index.AddCatalogue("40k", "Rust.cat", Encoding.UTF8.GetBytes(Rust));
        var log = new List<string>();

        IdReport.Write(Scraper.Table.Read(Table), index, new HashSet<string>(["shop.test"]), log.Add);

        CollectionAssert.Contains(log, "== 40k: 4 entries, 0 with ids, 4 gaps");
        CollectionAssert.Contains(log, "  1 x Captains | cat-iron se-captain");
        CollectionAssert.Contains(log, "  1 x Troopers | Iron Brothers cat-iron se-troopers defined; Rust Brothers cat-rust se-rust-troopers defined");
        CollectionAssert.Contains(log, "  1 x Wizard | wizard");
        CollectionAssert.Contains(log, "== name repair: 0 renamed, 1 removed, 0 box(es) removed");
    }
}
