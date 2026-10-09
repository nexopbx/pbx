// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Grpc.Net.Client;
using NexoPBX.Contracts.V1;

namespace NexoPBX.Api.Tests;

/// <summary>The optional TLS listener: HTTP/1.1 and HTTP/2 on one port, certificate loaded from PFX or PEM.</summary>
public sealed class HttpsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"nexopbx-tls-{Guid.NewGuid():N}");

    public HttpsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static (RSA Rsa, X509Certificate2 Cert) SelfSigned()
    {
        var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        return (key, request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
    }

    // The client trusts exactly the certificate the test generated (pinned by thumbprint), nothing else.
    private static async Task AssertServesBothProtocolsAsync(int port, string token, string thumbprint)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.Thumbprint == thumbprint };
        using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{port}"), DefaultRequestVersion = HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);

        using var channel = GrpcChannel.ForAddress($"https://127.0.0.1:{port}", new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler { SslOptions = { RemoteCertificateValidationCallback = (_, cert, _, _) => cert is X509Certificate2 c ? c.Thumbprint == thumbprint : new X509Certificate2(cert!).Thumbprint == thumbprint } } });
        var info = await new SystemService.SystemServiceClient(channel).GetInfoAsync(new GetInfoRequest(), HostFixture.Bearer(token));
        Assert.Equal("NexoPBX", info.Product);
    }

    [Fact]
    public async Task A_pfx_certificate_serves_http1_and_http2_on_the_same_port()
    {
        var (rsa, certificate) = SelfSigned();
        using (rsa)
        using (certificate)
        {
            var pfx = Path.Combine(_dir, "server.pfx");
            await File.WriteAllBytesAsync(pfx, certificate.Export(X509ContentType.Pfx, "secret"));
            var port = FreePort();

            var host = new HostFixture().With("server:https_listen", $"127.0.0.1:{port}").With("server:certificate_path", pfx).With("server:certificate_password", "secret");
            await host.InitializeAsync();
            try
            {
                await AssertServesBothProtocolsAsync(port, await host.LoginAsOwnerAsync(), certificate.Thumbprint);
            }
            finally
            {
                await host.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task A_pem_certificate_with_a_separate_key_file_works_too()
    {
        var (rsa, certificate) = SelfSigned();
        using (rsa)
        using (certificate)
        {
            var certPath = Path.Combine(_dir, "server.pem");
            var keyPath = Path.Combine(_dir, "server.key");
            await File.WriteAllTextAsync(certPath, certificate.ExportCertificatePem());
            await File.WriteAllTextAsync(keyPath, rsa.ExportPkcs8PrivateKeyPem());
            var port = FreePort();

            var host = new HostFixture().With("server:https_listen", $"127.0.0.1:{port}").With("server:certificate_path", certPath).With("server:certificate_key_path", keyPath);
            await host.InitializeAsync();
            try
            {
                await AssertServesBothProtocolsAsync(port, await host.LoginAsOwnerAsync(), certificate.Thumbprint);
            }
            finally
            {
                await host.DisposeAsync();
            }
        }
    }
}
