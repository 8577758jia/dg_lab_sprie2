using System.Reflection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace DgLabSocketSpire2.Bridge;

internal static class WaveLibrary
{
    private static readonly Dictionary<string, string[]> FallbackWaves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["呼吸"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A14141414",
            "0A0A0A0A28282828",
            "0A0A0A0A3C3C3C3C",
            "0A0A0A0A50505050",
            "0A0A0A0A64646464",
            "0A0A0A0A64646464",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000"
        },
        ["潮汐"] = new[]
        {
            "0A0A0A0A00000000",
            "0B0B0B0B10101010",
            "0D0D0D0D21212121",
            "0E0E0E0E32323232",
            "1010101042424242",
            "1212121253535353",
            "1313131364646464",
            "151515155C5C5C5C",
            "1616161654545454",
            "181818184C4C4C4C",
            "1A1A1A1A44444444",
            "0A0A0A0A00000000"
        },
        ["连击"] = new[]
        {
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A42424242",
            "0A0A0A0A21212121",
            "0A0A0A0A00000000",
            "0A0A0A0A00000000",
            "0A0A0A0A00000000"
        },
        ["快速按捏"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464"
        },
        ["按捏渐强"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A1C1C1C1C",
            "0A0A0A0A00000000",
            "0A0A0A0A34343434",
            "0A0A0A0A00000000",
            "0A0A0A0A49494949",
            "0A0A0A0A00000000",
            "0A0A0A0A57575757",
            "0A0A0A0A00000000",
            "0A0A0A0A64646464"
        },
        ["心跳节奏"] = new[]
        {
            "7070707064646464",
            "7070707064646464",
            "7070707064646464",
            "0A0A0A0A00000000",
            "0A0A0A0A4B4B4B4B",
            "0A0A0A0A53535353",
            "0A0A0A0A5B5B5B5B",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000"
        },
        ["压缩"] = new[]
        {
            "4A4A4A4A64646464",
            "4545454564646464",
            "4040404064646464",
            "3B3B3B3B64646464",
            "3636363664646464",
            "3232323264646464",
            "2D2D2D2D64646464",
            "2828282864646464",
            "2323232364646464",
            "1E1E1E1E64646464",
            "1A1A1A1A64646464",
            "0A0A0A0A64646464"
        },
        ["颗粒摩擦"] = new[]
        {
            "0A0A0A0A64646464",
            "0B0B0B0B64646464",
            "0D0D0D0D64646464",
            "0F0F0F0F00000000",
            "0F0F0F0F64646464",
            "1111111164646464",
            "1313131364646464",
            "1414141400000000",
            "1414141464646464",
            "1616161664646464",
            "1818181864646464",
            "1A1A1A1A00000000"
        },
        ["变速敲击"] = new[]
        {
            "1818181864646464",
            "1818181864646464",
            "1818181800000000",
            "1818181800000000",
            "1818181864646464",
            "1818181864646464",
            "1818181800000000",
            "1818181800000000",
            "7070707064646464",
            "7070707064646464",
            "7070707064646464"
        },
        ["节奏步伐"] = new[]
        {
            "0A0A0A0A00000000",
            "0A0A0A0A14141414",
            "0A0A0A0A28282828",
            "0A0A0A0A3C3C3C3C",
            "0A0A0A0A50505050",
            "0A0A0A0A64646464",
            "0A0A0A0A00000000",
            "0A0A0A0A19191919",
            "0A0A0A0A32323232",
            "0A0A0A0A4B4B4B4B",
            "0A0A0A0A64646464"
        }
    };

    private static readonly object Gate = new();
    private static Dictionary<string, string[]> _waves = new(FallbackWaves, StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    /// <summary>
    /// 官方 .pulse 频率滑块映射（滑块值 -> 脉冲周期 ms），来自 DG-BBS 官方格式文档。
    /// </summary>
    private static readonly int[] FreqSliderValueMap = BuildFreqSliderValueMap();

    /// <summary>
    /// 官方 .pulse 小节时长滑块映射（滑块值 -> 毫秒），来自 DG-BBS 官方格式文档。
    /// </summary>
    private static readonly int[] SectionTimeMsMap = BuildSectionTimeMsMap();

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            ReloadInternal();
            _initialized = true;
        }
    }

    public static void Reload()
    {
        lock (Gate)
        {
            ReloadInternal();
        }
    }

    public static string[] GetFrames(string waveName)
    {
        lock (Gate)
        {
            return _waves.TryGetValue(waveName, out var frames)
                ? frames
                : _waves["连击"];
        }
    }

    public static IReadOnlyCollection<string> Names
    {
        get
        {
            lock (Gate)
            {
                return _waves.Keys.OrderBy(static name => name).ToArray();
            }
        }
    }

    private static void ReloadInternal()
    {
        _waves = new Dictionary<string, string[]>(FallbackWaves, StringComparer.OrdinalIgnoreCase);
        var rootDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        LoadOfficialFile(Path.Combine(rootDir, "official_waves.wave"));
        LoadCustomDirectory(Path.Combine(rootDir, "waves"));
        ModLog.Info($"Wave library loaded: {_waves.Count} waves.");
    }

    private static void LoadOfficialFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<OfficialWaveEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                var frames = entry.ExpectedV3 is { Length: > 0 }
                    ? entry.ExpectedV3
                    : TryParsePulse(entry.Raw, out var parsed) ? parsed : null;
                if (frames is { Length: > 0 })
                {
                    _waves[entry.Name] = frames;
                }
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to load official waves file: {ex.Message}");
        }
    }

    private static void LoadCustomDirectory(string dirPath)
    {
        try
        {
            Directory.CreateDirectory(dirPath);
            foreach (var file in Directory.GetFiles(dirPath, "*.wave", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    switch (doc.RootElement.ValueKind)
                    {
                        case JsonValueKind.Object:
                            LoadCustomWaveObject(file, doc.RootElement);
                            break;
                        case JsonValueKind.Array:
                            LoadCustomWaveArray(file, doc.RootElement);
                            break;
                        default:
                            ModLog.Warn($"Skipped custom wave file {Path.GetFileName(file)}: JSON root must be an object or an array.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"Failed to load custom wave file {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.pulse", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (!TryParsePulse(content, out var frames))
                    {
                        ModLog.Warn($"Skipped custom pulse file {Path.GetFileName(file)}: not a valid 'Dungeonlab+pulse:' file or has no enabled section.");
                        continue;
                    }

                    _waves[Path.GetFileNameWithoutExtension(file)] = frames;
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"Failed to load custom pulse file {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to load custom wave directory: {ex.Message}");
        }
    }

    private static void LoadCustomWaveObject(string file, JsonElement root)
    {
        var name = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? Path.GetFileNameWithoutExtension(file)
            : Path.GetFileNameWithoutExtension(file);
        var frames = ReadFrames(root, "frames") ?? ReadFrames(root, "expectedV3");
        if (string.IsNullOrWhiteSpace(name) || frames == null || frames.Length == 0)
        {
            ModLog.Warn($"Skipped custom wave file {Path.GetFileName(file)}: missing 'name' or frames ('frames' / 'expectedV3' array).");
            return;
        }

        _waves[name] = frames;
    }

    private static void LoadCustomWaveArray(string file, JsonElement root)
    {
        var entries = root.EnumerateArray().ToArray();
        if (entries.Length > 0 && entries[0].ValueKind == JsonValueKind.Object)
        {
            // Official .wave format: array of { name, raw, expectedV2, expectedV3 } entries.
            var loaded = 0;
            foreach (var entry in entries)
            {
                var name = entry.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                var frames = ReadFrames(entry, "expectedV3") ?? ReadFrames(entry, "frames");
                if (string.IsNullOrWhiteSpace(name) || frames == null || frames.Length == 0)
                {
                    continue;
                }

                _waves[name] = frames;
                loaded++;
            }

            if (loaded == 0)
            {
                ModLog.Warn($"Skipped custom wave file {Path.GetFileName(file)}: no entries with 'name' and frames ('expectedV3' / 'frames' array).");
            }

            return;
        }

        // Simple format: the array itself is the list of wave frames.
        var simpleFrames = ReadFrames(root, null);
        if (simpleFrames == null || simpleFrames.Length == 0)
        {
            ModLog.Warn($"Skipped custom wave file {Path.GetFileName(file)}: no wave frames found.");
            return;
        }

        _waves[Path.GetFileNameWithoutExtension(file)] = simpleFrames;
    }

    private static string[]? ReadFrames(JsonElement element, string? propertyName)
    {
        if (propertyName != null)
        {
            if (!element.TryGetProperty(propertyName, out var framesElement) || framesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            element = framesElement;
        }

        return element.EnumerateArray()
            .Select(static frame => frame.ValueKind == JsonValueKind.String ? frame.GetString() ?? string.Empty : string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    /// <summary>
    /// 解析官方郊狼导出的 .pulse 文本（"Dungeonlab+pulse:..."），展开为 100ms 一帧的
    /// 8 字节 hex 帧（前 4 字节频率、后 4 字节强度）。算法逐帧对照官方 16 个内置波形验证一致。
    /// </summary>
    private static bool TryParsePulse(string content, out string[] frames)
    {
        frames = Array.Empty<string>();
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("Dungeonlab+pulse:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = trimmed.Substring("Dungeonlab+pulse:".Length);
        var sectionParts = body.Split(new[] { "+section+" }, StringSplitOptions.None);
        if (sectionParts.Length == 0 || string.IsNullOrWhiteSpace(sectionParts[0]))
        {
            return false;
        }

        var firstPart = sectionParts[0];
        var equalIndex = firstPart.IndexOf('=');
        if (equalIndex < 0)
        {
            return false;
        }

        var prefix = firstPart.Substring(0, equalIndex).Split(',');
        if (prefix.Length < 2 || !int.TryParse(prefix[0].Trim(), out var restSlider) || !int.TryParse(prefix[1].Trim(), out var speedFactor))
        {
            return false;
        }

        // 速度倍率 1/2/4：每个源脉冲在 100ms 窗口内重复的次数（4/倍率），默认按 1 处理。
        var pointRepeatNum = speedFactor switch
        {
            2 => 2,
            4 => 1,
            _ => 4
        };

        var allSectionData = new List<string> { firstPart.Substring(equalIndex + 1) };
        allSectionData.AddRange(sectionParts.Skip(1));

        var sections = new List<(int A, int B, int C, int Pc, double[] Shape)>();
        foreach (var sectionData in allSectionData)
        {
            if (string.IsNullOrWhiteSpace(sectionData))
            {
                continue;
            }

            var slashIndex = sectionData.IndexOf('/');
            if (slashIndex < 0)
            {
                return false;
            }

            var header = sectionData.Substring(0, slashIndex).Split(',');
            if (header.Length < 5 ||
                !int.TryParse(header[0].Trim(), out var freqA) ||
                !int.TryParse(header[1].Trim(), out var freqB) ||
                !int.TryParse(header[2].Trim(), out var sectionTime) ||
                !int.TryParse(header[3].Trim(), out var freqMode) ||
                !int.TryParse(header[4].Trim(), out var enabled))
            {
                return false;
            }

            if (enabled == 0)
            {
                continue;
            }

            var shapeTokens = sectionData.Substring(slashIndex + 1).Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (shapeTokens.Length < 2)
            {
                return false;
            }

            var shape = new double[shapeTokens.Length];
            for (var i = 0; i < shapeTokens.Length; i++)
            {
                var strengthToken = shapeTokens[i].Split('-')[0].Trim();
                if (!double.TryParse(strengthToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var strength))
                {
                    return false;
                }

                shape[i] = strength;
            }

            sections.Add((freqA, freqB, sectionTime, freqMode, shape));
        }

        if (sections.Count == 0)
        {
            return false;
        }

        var points = new List<(byte Freq, byte Value)>();
        foreach (var section in sections)
        {
            var freqAMs = FreqSliderValueMap[Math.Clamp(section.A, 0, FreqSliderValueMap.Length - 1)];
            var freqBMs = FreqSliderValueMap[Math.Clamp(section.B, 0, FreqSliderValueMap.Length - 1)];
            var sectionTimeMs = SectionTimeMsMap[Math.Clamp(section.C, 0, SectionTimeMsMap.Length - 1)];
            var barCount = section.Shape.Length;
            var repeat = Math.Max(1, (int)Math.Ceiling(sectionTimeMs / (double)(barCount * 100)));

            // 强度：竖条强度向下取整（官方导出即如此），按小节重复播放。
            var values = new int[barCount * repeat];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (int)Math.Floor(section.Shape[i % barCount]);
            }

            // 频率（ms）：
            // 1=固定（恒为 A）；2=节内渐变；3=元内渐变；4=元间渐变。
            var freqs = new int[barCount * repeat];
            var writeIndex = 0;
            switch (section.Pc)
            {
                case 2:
                    for (var e = 0; e < repeat; e++)
                    {
                        for (var j = 0; j < barCount; j++)
                        {
                            var t = (e + j / (double)(barCount - 1)) / repeat;
                            freqs[writeIndex++] = (int)Math.Floor(freqAMs + (freqBMs - freqAMs) * t);
                        }
                    }
                    break;
                case 3:
                    for (var e = 0; e < repeat; e++)
                    {
                        for (var j = 0; j < barCount; j++)
                        {
                            var t = j / (double)(barCount - 1);
                            freqs[writeIndex++] = (int)Math.Floor(freqAMs + (freqBMs - freqAMs) * t);
                        }
                    }
                    break;
                case 4:
                    for (var e = 0; e < repeat; e++)
                    {
                        var t = repeat > 1 ? e / (double)(repeat - 1) : 0;
                        var freq = (int)Math.Floor(freqAMs + (freqBMs - freqAMs) * t);
                        for (var j = 0; j < barCount; j++)
                        {
                            freqs[writeIndex++] = freq;
                        }
                    }
                    break;
                default:
                    Array.Fill(freqs, freqAMs);
                    break;
            }

            for (var i = 0; i < values.Length; i++)
            {
                var freqByte = EncodeFreq(freqs[i]);
                var valueByte = (byte)Math.Clamp(values[i], 0, 100);
                for (var k = 0; k < pointRepeatNum; k++)
                {
                    points.Add((freqByte, valueByte));
                }
            }
        }

        // 休息段：滑块 X 对应 ceil(X/10) 个 0.1s 帧（与官方导出行为一致）。
        var sleepPointCount = (restSlider > 0 ? (int)Math.Ceiling(restSlider / 10.0) : 0) * 4;
        for (var i = 0; i < sleepPointCount; i++)
        {
            points.Add((10, 0));
        }

        // 补齐为 4 的倍数（每帧 4 个 25ms 点）。
        while (points.Count % 4 != 0)
        {
            points.Add((10, 0));
        }

        var result = new string[points.Count / 4];
        for (var i = 0; i < result.Length; i++)
        {
            var sb = new StringBuilder(16);
            for (var k = 0; k < 4; k++)
            {
                sb.Append(points[i * 4 + k].Freq.ToString("X2"));
            }
            for (var k = 0; k < 4; k++)
            {
                sb.Append(points[i * 4 + k].Value.ToString("X2"));
            }
            result[i] = sb.ToString();
        }

        frames = result;
        return result.Length > 0;
    }

    /// <summary>
    /// 频率 ms -> 协议字节（与 dgLabFreqToUint8 一致，100ms 以下一一对应，以上分段压缩）。
    /// </summary>
    private static byte EncodeFreq(int freqMs)
    {
        if (freqMs == 0)
        {
            return 0;
        }

        if (freqMs < 10)
        {
            return 10;
        }

        if (freqMs <= 100)
        {
            return (byte)freqMs;
        }

        if (freqMs <= 600)
        {
            return (byte)((freqMs - 100) / 5 + 100);
        }

        if (freqMs <= 1000)
        {
            return (byte)((freqMs - 600) / 10 + 200);
        }

        return 0;
    }

    private static int[] BuildFreqSliderValueMap()
    {
        var list = new List<int>();
        for (var i = 10; i < 50; i++) list.Add(i);
        for (var i = 50; i < 80; i += 2) list.Add(i);
        for (var i = 80; i < 100; i += 5) list.Add(i);
        for (var i = 100; i < 200; i += 10) list.Add(i);
        list.AddRange(new[] { 200, 233, 266, 300, 333, 366 });
        for (var i = 400; i < 600; i += 50) list.Add(i);
        for (var i = 600; i <= 1000; i += 100) list.Add(i);
        return list.ToArray();
    }

    private static int[] BuildSectionTimeMsMap()
    {
        var list = new List<int>();
        for (var i = 1; i <= 49; i++) list.Add(i * 100);
        for (var i = 0; i < 15; i++) list.Add(5000 + i * 200);
        list.AddRange(new[] { 8000, 8500, 9000, 9500 });
        for (var i = 10; i < 20; i++) list.Add(i * 1000);
        list.AddRange(new[] { 20000, 23400, 26600, 30000, 33400, 36600 });
        list.AddRange(new[] { 40000, 45000, 50000, 55000 });
        list.AddRange(new[] { 60000, 70000, 80000, 90000 });
        list.AddRange(new[] { 100000, 120000, 140000, 160000, 180000 });
        list.AddRange(new[] { 200000, 250000, 300000 });
        return list.ToArray();
    }

    private sealed class OfficialWaveEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("raw")]
        public string Raw { get; set; } = string.Empty;

        [JsonPropertyName("expectedV3")]
        public string[] ExpectedV3 { get; set; } = Array.Empty<string>();
    }
}
