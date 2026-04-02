// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Text;

public static class SteamPairing
{
    private const string PairHtml = """
        <html lang="en">
        <head>
            <title>RustPlus Pairing</title>
        </head>
        <body>
        <div>To pair rustplus.js with Rust+, you must allow popup windows. You may need to refresh this page after enabling popups.</div>
        <script type="text/javascript">
            var popupWindow = window.open("https://companion-rust.facepunch.com/login", "", "");
            var handlerInterval = setInterval(function() {
                if(popupWindow.ReactNativeWebView === undefined){
                    console.log("registering ReactNativeWebView.postMessage handler");
                    popupWindow.ReactNativeWebView = {
                        postMessage: function(message) {
                            clearInterval(handlerInterval);
                            var auth = JSON.parse(message);
                            window.location.href = "http://localhost:3000/callback?token=" + encodeURIComponent(auth.Token);
                            popupWindow.close();
                        },
                    };
                }
            }, 250);
        </script>
        </body>
        </html>
        """;

    public static async Task<string> LinkSteamWithRustPlusAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string>();
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:3000/");
        listener.Start();

        Process? chromeProcess = null;
        try
        {
            chromeProcess = LaunchChrome("http://localhost:3000");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch Google Chrome. Do you have it installed?");
            Environment.Exit(1);
        }

        using var reg = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled();
            try { listener.Stop(); } catch { }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening && !cancellationToken.IsCancellationRequested)
                {
                    var context = await listener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    if (request.Url?.AbsolutePath == "/callback")
                    {
                        var token = request.QueryString["token"];
                        if (!string.IsNullOrEmpty(token))
                        {
                            var message = "Steam Account successfully linked with rustplus.js, you can now close this window and go back to the console.";
                            var buffer = Encoding.UTF8.GetBytes(message);
                            response.ContentType = "text/plain";
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, cancellationToken);
                            response.Close();
                            tcs.TrySetResult(token);
                        }
                        else
                        {
                            var message = "Token missing from request!";
                            var buffer = Encoding.UTF8.GetBytes(message);
                            response.StatusCode = 400;
                            response.ContentType = "text/plain";
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, cancellationToken);
                            response.Close();
                            tcs.TrySetException(new InvalidOperationException(message));
                        }

                        KillChrome(chromeProcess);
                        try { listener.Stop(); } catch { }
                        return;
                    }
                    else
                    {
                        var buffer = Encoding.UTF8.GetBytes(PairHtml);
                        response.ContentType = "text/html";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, cancellationToken);
                        response.Close();
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (HttpListenerException) { }
        }, cancellationToken);

        return await tcs.Task;
    }

    private static Process LaunchChrome(string url)
    {
        var chromePaths = new[]
        {
            // Windows
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe"),
            // Linux
            "/usr/bin/google-chrome",
            "/usr/bin/chromium-browser",
            // macOS
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        };

        var chromePath = chromePaths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Google Chrome not found.");

        var userDataDir = Path.Combine(Path.GetTempPath(), "rustplus-csharp-chrome-profile");

        var args = string.Join(' ',
        [
            "--disable-web-security",
            "--disable-popup-blocking",
            "--disable-site-isolation-trials",
            $"--user-data-dir={userDataDir}",
            url
        ]);

        return Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = args,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start Chrome process.");
    }

    private static void KillChrome(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }
}
