using NTwain;
using NTwain.Data;
using System.IO;
using System.Reflection;

namespace SmartScanner.Services;

public class ScannerService : IScannerService
{
    // WIA constants
    private const int WIA_DEVICE_TYPE_SCANNER = 1;
    private const int WIA_DIP_DEV_NAME = 7;
    private const int WIA_IPS_CUR_INTENT = 6146;
    private const int WIA_IPS_XRES = 4114;
    private const int WIA_IPS_YRES = 4116;
    private const string BMP_FORMAT = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";

    // NTwain app identity
    private static readonly TWIdentity AppId =
        TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());

    // Tracks whether each discovered scanner is TWAIN (true) or WIA (false)
    private readonly Dictionary<string, bool> _isTwain = new();

    public IList<string> GetAvailableScanners()
    {
        _isTwain.Clear();
        var names = new List<string>();

        // TWAIN sources
        try
        {
            var session = new TwainSession(AppId);
            if (session.Open() == ReturnCode.Success)
            {
                foreach (var src in session.GetSources())
                {
                    _isTwain[src.Name] = true;
                    names.Add(src.Name);
                }
                session.Close();
            }
        }
        catch { }

        // WIA devices (skip any already found via TWAIN)
        try
        {
            dynamic mgr = WiaCreate("WIA.DeviceManager");
            dynamic infos = mgr.DeviceInfos;
            for (int i = 1; i <= (int)infos.Count; i++)
            {
                dynamic info = infos[i];
                if ((int)info.Type != WIA_DEVICE_TYPE_SCANNER) continue;
                string? name = WiaGetProp(info.Properties, WIA_DIP_DEV_NAME);
                if (name != null && !_isTwain.ContainsKey(name))
                {
                    _isTwain[name] = false;
                    names.Add(name);
                }
            }
        }
        catch { }

        return names;
    }

    public Task<List<byte[]>> ScanAsync(string scannerName, int dpi, string colorMode, IntPtr windowHandle)
    {
        bool useTwain = _isTwain.TryGetValue(scannerName, out bool t) && t;
        return useTwain
            ? ScanTwainAsync(scannerName, dpi, colorMode, windowHandle)
            : ScanWiaAsync(scannerName, dpi, colorMode);
    }

    // ── TWAIN ────────────────────────────────────────────────────────────
    private static Task<List<byte[]>> ScanTwainAsync(string scannerName, int dpi, string colorMode, IntPtr windowHandle)
    {
        var tcs = new TaskCompletionSource<List<byte[]>>();
        var pages = new List<byte[]>();
        var session = new TwainSession(AppId);

        session.DataTransferred += (_, e) =>
        {
            if (e.NativeData != IntPtr.Zero)
            {
                using var stream = e.GetNativeImageStream();
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    pages.Add(ms.ToArray());
                }
            }
        };

        session.SourceDisabled += (_, _) =>
        {
            session.CurrentSource?.Close();
            session.Close();
            tcs.TrySetResult(pages);
        };

        session.TransferError += (_, e) =>
            tcs.TrySetException(new Exception($"TWAIN error: {e.Exception?.Message}"));

        try
        {
            if (session.Open() != ReturnCode.Success)
                throw new Exception("Could not open TWAIN session.");

            var source = session.GetSources().FirstOrDefault(x => x.Name == scannerName)
                         ?? throw new Exception($"TWAIN source '{scannerName}' not found.");

            if (source.Open() != ReturnCode.Success)
                throw new Exception("Could not open the scanner source.");

            source.Capabilities.ICapXResolution.SetValue((TWFix32)dpi);
            source.Capabilities.ICapYResolution.SetValue((TWFix32)dpi);

            var pixelType = colorMode switch
            {
                "Grayscale"  => PixelType.Gray,
                "BlackWhite" => PixelType.BlackWhite,
                _            => PixelType.RGB
            };
            source.Capabilities.ICapPixelType.SetValue(pixelType);

            source.Enable(SourceEnableMode.ShowUI, true, windowHandle);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    // ── WIA ──────────────────────────────────────────────────────────────
    private static Task<List<byte[]>> ScanWiaAsync(string scannerName, int dpi, string colorMode)
    {
        return Task.Run(() =>
        {
            dynamic mgr = WiaCreate("WIA.DeviceManager");
            dynamic? info = WiaFindDevice(mgr.DeviceInfos, scannerName)
                            ?? throw new Exception($"WIA device '{scannerName}' not found.");

            dynamic device = info.Connect();
            dynamic item   = device.Items[1];

            WiaSetProp(item.Properties, WIA_IPS_XRES, dpi);
            WiaSetProp(item.Properties, WIA_IPS_YRES, dpi);

            int intent = colorMode switch
            {
                "Grayscale"  => 2,
                "BlackWhite" => 4,
                _            => 1
            };
            WiaSetProp(item.Properties, WIA_IPS_CUR_INTENT, intent);

            dynamic imageFile = item.Transfer(BMP_FORMAT);
            string  temp      = Path.ChangeExtension(Path.GetTempFileName(), ".bmp");
            imageFile.SaveFile(temp);
            var bytes = File.ReadAllBytes(temp);
            File.Delete(temp);
            return new List<byte[]> { bytes };
        });
    }

    // ── WIA helpers ───────────────────────────────────────────────────────
    private static dynamic WiaCreate(string progId)
    {
        var t = Type.GetTypeFromProgID(progId)
                ?? throw new Exception("WIA is not available on this system.");
        return Activator.CreateInstance(t)!;
    }

    private static dynamic? WiaFindDevice(dynamic infos, string name)
    {
        for (int i = 1; i <= (int)infos.Count; i++)
        {
            dynamic info = infos[i];
            if ((int)info.Type != WIA_DEVICE_TYPE_SCANNER) continue;
            if (WiaGetProp(info.Properties, WIA_DIP_DEV_NAME) == name) return info;
        }
        return null;
    }

    private static string? WiaGetProp(dynamic props, int id)
    {
        for (int i = 1; i <= (int)props.Count; i++)
        {
            dynamic p = props[i];
            if ((int)p.PropertyID == id) return (string)p.Value;
        }
        return null;
    }

    private static void WiaSetProp(dynamic props, int id, int value)
    {
        for (int i = 1; i <= (int)props.Count; i++)
        {
            dynamic p = props[i];
            if ((int)p.PropertyID == id) { p.Value = value; return; }
        }
    }
}
