using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>A GitHub answer that is not a success. Names the request and the status, never the token.</summary>
public sealed class GitHubException(string message) : Exception(message);

/// <summary>
/// This repository through the GitHub REST API (ADR 0003 §3): reads the table, lists the open scraper pull requests,
/// opens a draft pull request. It never merges. The token, when present, goes only into the Authorization header.
/// </summary>
public sealed class GitHubRepository(HttpClient http, string? token) : IRepository
{
    public const string Repository = "LordTyberias/armamentarium-box-data";
    public const string BranchPrefix = "scraper/";

    private const string File = "box-contents.json";
    private static readonly Uri Api = new("https://api.github.com/");

    public async Task<(string Text, string Sha)> GetTableAsync(CancellationToken cancellationToken)
    {
        using var file = await SendAsync(HttpMethod.Get, $"repos/{Repository}/contents/{File}?ref=main", null, cancellationToken);
        var root = file.RootElement;
        var content = root.GetProperty("content").GetString()!.Replace("\n", string.Empty);
        return (Encoding.UTF8.GetString(Convert.FromBase64String(content)), root.GetProperty("sha").GetString()!);
    }

    public async Task<IReadOnlyList<string>> OpenScraperBranchesAsync(CancellationToken cancellationToken)
    {
        using var pulls = await SendAsync(HttpMethod.Get, $"repos/{Repository}/pulls?state=open&per_page=100", null, cancellationToken);
        return pulls.RootElement.EnumerateArray()
            .Select(p => p.GetProperty("head").GetProperty("ref").GetString())
            .OfType<string>()
            .Where(r => r.StartsWith(BranchPrefix, StringComparison.Ordinal))
            .ToList();
    }

    public async Task ProposeAsync(Proposal proposal, string tableSha, CancellationToken cancellationToken)
    {
        string mainSha;
        using (var main = await SendAsync(HttpMethod.Get, $"repos/{Repository}/git/ref/heads/main", null, cancellationToken))
        {
            mainSha = main.RootElement.GetProperty("object").GetProperty("sha").GetString()!;
        }

        (await SendAsync(HttpMethod.Post, $"repos/{Repository}/git/refs", new JsonObject
        {
            ["ref"] = $"refs/heads/{proposal.Branch}",
            ["sha"] = mainSha,
        }, cancellationToken)).Dispose();

        (await SendAsync(HttpMethod.Put, $"repos/{Repository}/contents/{File}", new JsonObject
        {
            ["message"] = proposal.Title,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(proposal.Table)),
            ["sha"] = tableSha,
            ["branch"] = proposal.Branch,
        }, cancellationToken)).Dispose();

        (await SendAsync(HttpMethod.Post, $"repos/{Repository}/pulls", new JsonObject
        {
            ["title"] = proposal.Title,
            ["head"] = proposal.Branch,
            ["base"] = "main",
            ["body"] = proposal.Body,
            ["draft"] = true,
        }, cancellationToken)).Dispose();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(Api, path));
        request.Headers.UserAgent.ParseAdd("ArmamentariumBoxData");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubException(
                $"GitHub {method} {path} answered {(int)response.StatusCode}: {text[..Math.Min(text.Length, 300)]}");
        }

        return JsonDocument.Parse(text);
    }
}
