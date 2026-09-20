using System.Net;
using System.Net.Sockets;
using System.Text;
using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Infrastructure.Notificacoes;

namespace FiapX.VideoManagement.Tests;

public sealed class EnviadorNotificacaoSmtpTests
{
    [Fact]
    public async Task Sends_failure_notification_to_configured_smtp_server()
    {
        await using var smtp = await FakeSmtpServer.StartAsync();
        var sender = new EnviadorNotificacaoSmtp(
            new OpcoesNotificacaoSmtp
            {
                Host = IPAddress.Loopback.ToString(),
                Port = smtp.Port,
                From = "no-reply@fiapx.local",
                Timeout = TimeSpan.FromSeconds(5)
            });
        var videoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await sender.EnviarFalhaProcessamentoAsync(
            new NotificacaoFalhaProcessamento(
                "alice@fiapx.local",
                videoId,
                "PROCESSING_FAILED",
                "Falha segura para o usuario."),
            CancellationToken.None);

        var envelope = await smtp.Envelope;
        var message = DecodeSmtpMessage(await smtp.MessageData);

        Assert.Contains("MAIL FROM:<no-reply@fiapx.local>", envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RCPT TO:<alice@fiapx.local>", envelope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Falha segura para o usuario.", message, StringComparison.Ordinal);
        Assert.Contains(videoId.ToString(), message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_smtp_message_accepts_lf_only_quoted_printable_payload()
    {
        var rawMessage = """
            MIME-Version: 1.0
            Content-Type: text/plain; charset=utf-8
            Content-Transfer-Encoding: quoted-printable

            Falha segura para o usuario.=0D=0A=0D=0AVideoId: 11111111-1111-1111-1111-111111111111
            """;

        var message = DecodeSmtpMessage(rawMessage);

        Assert.Contains("Falha segura para o usuario.", message, StringComparison.Ordinal);
        Assert.Contains("11111111-1111-1111-1111-111111111111", message, StringComparison.Ordinal);
    }

    private static string DecodeSmtpMessage(string rawMessage)
    {
        var normalized = rawMessage.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var bodyStart = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        var body = bodyStart >= 0 ? normalized[(bodyStart + 2)..] : normalized;

        if (!normalized.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Contains("Content-Transfer-Encoding: quoted-printable", StringComparison.OrdinalIgnoreCase)
                ? DecodeQuotedPrintable(body)
                : body;
        }

        var base64 = string.Concat(
            body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("--", StringComparison.Ordinal)));

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string DecodeQuotedPrintable(string value)
    {
        var bytes = new List<byte>(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '=' || index + 2 >= value.Length)
            {
                bytes.Add((byte)value[index]);
                continue;
            }

            if (value[index + 1] == '\n')
            {
                index++;
                continue;
            }

            if (value[index + 1] == '\r' && value[index + 2] == '\n')
            {
                index += 2;
                continue;
            }

            var hex = value.AsSpan(index + 1, 2);
            if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var decoded))
            {
                bytes.Add(decoded);
                index += 2;
                continue;
            }

            bytes.Add((byte)value[index]);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serverTask;
        private readonly TaskCompletionSource<string> _envelope = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _messageData = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private FakeSmtpServer(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = RunAsync();
        }

        public int Port { get; }
        public Task<string> Envelope => _envelope.Task;
        public Task<string> MessageData => _messageData.Task;

        public static Task<FakeSmtpServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeSmtpServer(listener));
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();

            try
            {
                await _serverTask;
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task RunAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
            var envelope = new StringBuilder();

            await writer.WriteLineAsync("220 fiapx.local SMTP ready");

            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteAsync("250-fiapx.local\r\n250 OK\r\n");
                    await writer.FlushAsync();
                    continue;
                }

                if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    envelope.AppendLine(line);
                    await writer.WriteLineAsync("250 OK");
                    continue;
                }

                if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var data = new StringBuilder();

                    while (await reader.ReadLineAsync() is { } dataLine)
                    {
                        if (dataLine == ".")
                        {
                            break;
                        }

                        data.AppendLine(dataLine);
                    }

                    _envelope.TrySetResult(envelope.ToString());
                    _messageData.TrySetResult(data.ToString());
                    await writer.WriteLineAsync("250 Message accepted");
                    continue;
                }

                if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 Bye");
                    break;
                }

                await writer.WriteLineAsync("250 OK");
            }
        }
    }
}
