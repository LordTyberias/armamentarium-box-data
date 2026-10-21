using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Armamentarium.BoxData.Scraper;

/// <summary>What curl did for one request: its exit code, what it wrote (headers, blank line, body) and its error text.</summary>
public sealed record CurlResult(int ExitCode, byte[] Output, string Error);

/// <summary>Runs curl with the given arguments.</summary>
public delegate Task<CurlResult> CurlRun(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

/// <summary>
/// Asks Shopify's product address (<c>/products/&lt;handle&gt;.js</c>) through curl, every other address through the inner
/// handler. From the server Shopify answers the .NET client 429 on that address after the first request, across every
/// storefront and for at least fifteen minutes, while curl over HTTP/2 from the same address gets 200 (measured 2026-10-21,
/// ADR 0003 Addendum ARMAM-655). curl goes as it is, under the service's own name; the answer comes back as a response like
/// any other, so <see cref="Fetcher"/> honours <c>Retry-After</c>, block pages and redirects the same way.
/// </summary>
public sealed class CurlHandler(HttpMessageHandler inner, CurlRun? run = null) : DelegatingHandler(inner)
{
    // The largest shop page measured is 1.4 MB (the client's buffer limit in Fetcher).
    private const string MaxFileSize = "16M";

    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();

    private readonly CurlRun _run = run ?? RunAsync;

    public static bool IsShopifyProduct(Uri uri) =>
        uri.AbsolutePath.StartsWith("/products/", StringComparison.Ordinal) &&
        uri.AbsolutePath.EndsWith(".js", StringComparison.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { } uri || !IsShopifyProduct(uri))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // No -L: the fetcher follows a redirect itself, within the host only.
        List<string> arguments = ["-sS", "--http2", "--compressed", "--max-filesize", MaxFileSize, "-D", "-"];
        foreach (var header in request.Headers)
        {
            arguments.Add("-H");
            arguments.Add($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        arguments.Add(uri.AbsoluteUri);
        var result = await _run(arguments, cancellationToken);
        return result.ExitCode switch
        {
            0 => Parse(result.Output, request),
            // CURLE_OPERATION_TIMEDOUT
            28 => throw new TaskCanceledException($"curl timed out: {result.Error.Trim()}"),
            _ => throw new HttpRequestException($"curl exited {result.ExitCode}: {result.Error.Trim()}"),
        };
    }

    private static HttpResponseMessage Parse(byte[] output, HttpRequestMessage request)
    {
        var end = output.AsSpan().IndexOf(HeaderEnd);
        if (end < 0)
        {
            throw new HttpRequestException("curl wrote no complete header block");
        }

        var lines = Encoding.ASCII.GetString(output, 0, end).Split("\r\n");
        var status = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (status.Length < 2 || !status[0].StartsWith("HTTP/", StringComparison.Ordinal) || !int.TryParse(status[1], out var code))
        {
            throw new HttpRequestException($"curl wrote no status line: {lines[0]}");
        }

        var version = status[0]["HTTP/".Length..];
        var response = new HttpResponseMessage((HttpStatusCode)code)
        {
            RequestMessage = request,
            Version = Version.Parse(version.Contains('.') ? version : version + ".0"),
            Content = new ByteArrayContent(output, end + HeaderEnd.Length, output.Length - end - HeaderEnd.Length),
        };

        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var (name, value) = (line[..colon], line[(colon + 1)..].Trim());
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    private static async Task<CurlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("curl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new HttpRequestException("curl did not start");
        }
        catch (Win32Exception ex)
        {
            throw new HttpRequestException($"curl did not start: {ex.Message}", ex);
        }

        using var _ = process;
        try
        {
            using var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(copy, error);
            await process.WaitForExitAsync(cancellationToken);
            return new CurlResult(process.ExitCode, output.ToArray(), await error);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
