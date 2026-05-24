namespace TerrariumController.Services
{
    internal static class LinuxGpioChipSelector
    {
        public static IEnumerable<int> GetCandidateChipIds(ILogger logger, int? configuredLinuxChipId)
        {
            if (configuredLinuxChipId.HasValue)
            {
                // Use the configured chip first, but still fall through to discovered chips
                // if the configured chip device file does not exist, to recover from misconfiguration.
                logger.LogInformation("Linux GPIO chip explicitly configured to gpiochip{ChipId}", configuredLinuxChipId.Value);
                var preferred = new List<int> { configuredLinuxChipId.Value };

                // Append remaining discovered chips as fallback candidates.
                try
                {
                    foreach (var path in Directory.EnumerateFiles("/dev", "gpiochip*"))
                    {
                        var fileName = Path.GetFileName(path);
                        if (fileName.StartsWith("gpiochip", StringComparison.OrdinalIgnoreCase)
                            && int.TryParse(fileName[8..], out var fallbackId)
                            && fallbackId != configuredLinuxChipId.Value)
                        {
                            preferred.Add(fallbackId);
                        }
                    }
                }
                catch { }

                return preferred;
            }

            var discoveredChipIds = new List<int>();

            try
            {
                foreach (var path in Directory.EnumerateFiles("/dev", "gpiochip*"))
                {
                    var fileName = Path.GetFileName(path);
                    if (fileName.StartsWith("gpiochip", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(fileName[8..], out var chipId))
                    {
                        discoveredChipIds.Add(chipId);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enumerate /dev/gpiochip* devices");
            }

            if (discoveredChipIds.Count == 0)
            {
                return new[] { 0 };
            }

            discoveredChipIds = discoveredChipIds.Distinct().ToList();

            if (IsRaspberryPi5())
            {
                // On Raspberry Pi 5, the 40-pin header is usually exposed via RP1 (commonly gpiochip4).
                return discoveredChipIds
                    .OrderByDescending(id => id == 4)
                    .ThenBy(id => id)
                    .ToList();
            }

            return discoveredChipIds.OrderBy(id => id).ToList();
        }

        private static bool IsRaspberryPi5()
        {
            try
            {
                const string modelPath = "/proc/device-tree/model";
                if (!File.Exists(modelPath))
                {
                    return false;
                }

                var model = File.ReadAllText(modelPath).Replace("\0", string.Empty);
                return model.Contains("Raspberry Pi 5", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}