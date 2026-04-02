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
            chromeProcess = LaunchBrowser("http://localhost:3000", logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch Chrome. Is Google Chrome installed?");
            throw;
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
                            var message = "Steam Account successfully linked with Rust+. You can now close this browser tab.";
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

                        KillProcess(chromeProcess);
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

    /// <summary>
    /// Launches a Chromium-based browser (Edge, Chrome, Chromium) with --disable-web-security
    /// so the localhost pairing page can inject ReactNativeWebView.postMessage into the
    /// cross-origin Facepunch login popup. This cannot work in a normal browser session
    /// due to the same-origin policy.
    /// </summary>
    private static Process LaunchBrowser(string url, ILogger logger)
    {
        var chromiumPaths = new[]
        {
            // Google Chrome
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe"),

            // Microsoft Edge (ships with Windows 10+)
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\Application\msedge.exe"),
            
            // Linux
            "/usr/bin/google-chrome",
            "/usr/bin/microsoft-edge",
            "/usr/bin/chromium-browser",
            
            // macOS
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
        };

        var browserPath = chromiumPaths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                "A Chromium-based browser (Edge or Chrome) is required for Steam account linking. " +
                "The Facepunch login page uses cross-origin messaging that only works " +
                "with the --disable-web-security flag. Please install Edge or Chrome and try again.");

        logger.LogInformation("Launching {Browser} with security flags disabled for Steam pairing.",
            Path.GetFileNameWithoutExtension(browserPath));

        var userDataDir = Path.Combine(Path.GetTempPath(), "rustplus-csharp-browser-profile");

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
            FileName = browserPath,
            Arguments = args,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(browserPath)}.");
    }

    private static void KillProcess(Process? process)
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
