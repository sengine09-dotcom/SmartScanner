namespace SmartScanner.Services;

public interface IScannerService
{
    IList<string> GetAvailableScanners();
    Task<List<byte[]>> ScanAsync(string scannerName, int dpi, string colorMode, IntPtr windowHandle);
}
