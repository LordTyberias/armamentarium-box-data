namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class ProductCatalogueTests
{
    private const string Json = """
        {"version":"x","products":[
          {"manufacturer":"games-workshop","productCode":"60010199002","ean":"5011921000002","name":"KILL TEAM: B","gameSystems":["Kill Team"],"category":"miniatures","status":"discontinued"},
          {"manufacturer":"games-workshop","productCode":"60010199001","ean":"5011921000001","name":"KILL TEAM: A","gameSystems":["Kill Team"],"category":"miniatures","status":"current"},
          {"manufacturer":"games-workshop","productCode":"99120101001","ean":"5011921000003","name":"COMBAT PATROL: X","gameSystems":["Warhammer 40,000"],"category":"miniatures","status":"current"},
          {"manufacturer":"games-workshop","productCode":"99120201001","ean":"5011921000004","name":"SPEARHEAD: Y","gameSystems":["Age of Sigmar"],"category":"miniatures","status":"current"},
          {"manufacturer":"games-workshop","productCode":"60040101001","ean":"5011921000005","name":"BOOK","gameSystems":["Warhammer 40,000"],"category":"book","status":"current"},
          {"manufacturer":"games-workshop","productCode":"99111401001","ean":"5011921000006","name":"HOBBIT","gameSystems":["Middle-earth"],"category":"miniatures","status":"current"},
          {"manufacturer":"warlord-games","productCode":"WGB-1","ean":"5060000000001","name":"OTHER","gameSystems":["Bolt Action"],"category":"miniatures","status":"current"},
          {"manufacturer":"games-workshop","productCode":"60010199009","name":"KILL TEAM: NO EAN","gameSystems":["Kill Team"],"category":"miniatures","status":"current"}
        ]}
        """;

    [TestMethod]
    public void Candidates_KeepMiniaturesOfAppSystems_InScanOrder()
    {
        var products = ProductCatalogue.Parse(Json);

        var codes = ProductCatalogue.Candidates(products, new HashSet<string> { "99120201001" }).Select(p => p.Code).ToArray();

        CollectionAssert.AreEqual(new[] { "60010199001", "60010199009", "60010199002", "99120101001" }, codes);
    }

    [TestMethod]
    public void ReadingsOf_MapsGameSystems()
    {
        var products = ProductCatalogue.Parse(Json);

        CollectionAssert.AreEqual(new[] { "kt" }, ProductCatalogue.ReadingsOf(products.First(p => p.Code == "60010199001")).ToArray());
        CollectionAssert.AreEqual(new[] { "aos" }, ProductCatalogue.ReadingsOf(products.First(p => p.Code == "99120201001")).ToArray());
    }

    [TestMethod]
    public void IsCombatPatrol_ByName()
    {
        var products = ProductCatalogue.Parse(Json);

        Assert.IsTrue(ProductCatalogue.IsCombatPatrol(products.First(p => p.Code == "99120101001")));
        Assert.IsFalse(ProductCatalogue.IsCombatPatrol(products.First(p => p.Code == "60010199001")));
    }
}
