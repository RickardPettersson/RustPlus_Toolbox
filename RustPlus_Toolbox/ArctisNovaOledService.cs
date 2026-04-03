using HidSharp;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace RustPlus_Toolbox
{
    /// <summary>
    /// Manages the Arctis Nova Pro Wireless base station OLED display (128x64).
    /// Communicates directly over USB HID on interface 4.
    /// </summary>
    public sealed class ArctisNovaOledService : IDisposable
    {
        private const int SteelSeriesVendorId = 0x1038;
        private const int ScreenWidth = 128;
        private const int ScreenHeight = 64;
        private const int ReportSize = 1024;
        private const int MaxStripWidth = 64;
        private const byte ReportId = 0x06;
        private const byte DrawCommand = 0x93;
        private const byte RestoreCommand = 0x95;

        private static readonly int[] ProductIds = [0x12E0, 0x12E5, 0x12CB, 0x12CD, 0x225D];

        private readonly ILogger _logger;
        private HidDevice? _device;
        private HidStream? _stream;
        private bool _disposed;

        public bool IsConnected => _stream != null;

        public ArctisNovaOledService(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Attempts to find and open the Arctis Nova Pro base station OLED device.
        /// Returns true if the device was found and opened successfully.
        /// </summary>
        public bool TryConnect()
        {
            if (_stream != null)
                return true;

            try
            {
                _device = DeviceList.Local.GetHidDevices()
                    .FirstOrDefault(d => d.VendorID == SteelSeriesVendorId
                        && ProductIds.Contains(d.ProductID)
                        && d.DevicePath.Contains("mi_04", StringComparison.OrdinalIgnoreCase));

                if (_device == null)
                {
                    _logger.LogDebug("Arctis Nova Pro base station not found.");
                    return false;
                }

                _stream = _device.Open();
                _logger.LogInformation("Arctis Nova Pro OLED connected (PID={ProductId:X4}).", _device.ProductID);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open Arctis Nova Pro OLED device.");
                _stream = null;
                _device = null;
                return false;
            }
        }

        /// <summary>
        /// Renders the in-game clock on the OLED display.
        /// Top half: large time (HH:MM), bottom: day/night indicator with sunrise/sunset times.
        /// </summary>
        public void UpdateDisplay(string timeText, bool isDay, string sunriseText, string sunsetText)
        {
            if (_stream == null)
                return;

            try
            {
                var pixels = RenderClockScreen(timeText, isDay, sunriseText, sunsetText);
                SendScreen(pixels);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Lost connection to Arctis Nova OLED. Will retry next tick.");
                CloseStream();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating Arctis Nova OLED display.");
            }
        }

        /// <summary>
        /// Restores the default base station display.
        /// </summary>
        public void RestoreDefaultDisplay()
        {
            if (_stream == null)
                return;

            try
            {
                var report = new byte[ReportSize];
                report[0] = ReportId;
                report[1] = RestoreCommand;
                SendFeatureReport(report);
                _logger.LogInformation("Arctis Nova OLED display restored to default.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore Arctis Nova OLED default display.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            RestoreDefaultDisplay();
            CloseStream();
        }

        // ── Rendering ──────────────────────────────────────────────────────

        private static bool[,] RenderClockScreen(string timeText, bool isDay, string sunriseText, string sunsetText)
        {
            var pixels = new bool[ScreenWidth, ScreenHeight];

            using var surface = SKSurface.Create(new SKImageInfo(ScreenWidth, ScreenHeight, SKColorType.Gray8));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);

            // Large time text - top portion
            using var timePaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = false,
                TextSize = 38,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyleWeight.Bold,
                    SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                SubpixelText = false
            };

            float timeWidth = timePaint.MeasureText(timeText);
            var timeMetrics = timePaint.FontMetrics;
            float timeX = (ScreenWidth - timeWidth) / 2f;
            float timeY = 36; // baseline for the large clock

            canvas.DrawText(timeText, timeX, timeY, timePaint);

            // Bottom line: day/night icon + sunrise/sunset info
            using var infoPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = false,
                TextSize = 11,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                SubpixelText = false
            };

            string dayNight = isDay ? "DAY" : "NIGHT";
            string infoLine = $"{dayNight}  {sunriseText}-{sunsetText}";
            float infoWidth = infoPaint.MeasureText(infoLine);
            float infoX = (ScreenWidth - infoWidth) / 2f;

            canvas.DrawText(infoLine, infoX, 58, infoPaint);

            canvas.Flush();

            // Convert to 1-bit
            using var image = surface.Snapshot();
            using var pixmap = image.PeekPixels();
            for (int px = 0; px < ScreenWidth; px++)
            {
                for (int py = 0; py < ScreenHeight; py++)
                {
                    var color = pixmap.GetPixelColor(px, py);
                    pixels[px, py] = color.Red > 128;
                }
            }

            return pixels;
        }

        // ── USB HID ────────────────────────────────────────────────────────

        private void SendScreen(bool[,] pixels)
        {
            SendStrip(pixels, 0, 0, MaxStripWidth, ScreenHeight);
            SendStrip(pixels, MaxStripWidth, 0, MaxStripWidth, ScreenHeight);
        }

        private void SendStrip(bool[,] pixels, int x, int y, int w, int h)
        {
            int paddedH = ((h + (y % 8) + 7) / 8) * 8;
            var report = new byte[ReportSize];
            report[0] = ReportId;
            report[1] = DrawCommand;
            report[2] = (byte)x;
            report[3] = (byte)y;
            report[4] = (byte)w;
            report[5] = (byte)paddedH;

            for (int px = 0; px < w; px++)
            {
                for (int py = 0; py < h; py++)
                {
                    if (pixels[x + px, y + py])
                    {
                        int ri = px * paddedH + py;
                        report[(ri / 8) + 6] |= (byte)(1 << (ri % 8));
                    }
                }
            }

            SendFeatureReport(report);
        }

        private void SendFeatureReport(byte[] report)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    _stream!.SetFeature(report);
                    return;
                }
                catch (IOException) when (attempt < 9)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private void CloseStream()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            _device = null;
        }
    }
}
